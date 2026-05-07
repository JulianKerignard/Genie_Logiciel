using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace EasyLog;

/// <summary>
/// Writes <see cref="LogEntry"/> instances to a daily JSON file
/// named "yyyy-MM-dd.json" inside a configurable directory.
/// </summary>
/// <remarks>
/// <para>
/// Concurrent <see cref="Append"/> calls (V3 multi-job runs) are funneled
/// through an in-memory <see cref="Channel{T}"/> consumed by a single writer
/// task. The writer drains everything currently queued, groups by day file,
/// updates a per-day in-memory cache, and writes the file atomically — so
/// 4 jobs publishing 1000 entries concurrently triggers a handful of file
/// writes instead of 4000.
/// </para>
/// <para>
/// <see cref="Append"/> stays <c>void</c> and synchronous: each call blocks
/// until its entry has been flushed (or the writer reports an error), so the
/// v1.0 durability contract is preserved — no log loss on crash between
/// Append return and the next backup step.
/// </para>
/// <para>
/// Order is preserved per caller: a thread that calls Append(A1) then
/// Append(A2) sees A1 strictly before A2 in the day file. Cross-thread order
/// is not guaranteed by design (no global write barrier between threads).
/// </para>
/// </remarks>
public sealed class JsonDailyLogger : IDailyLogger, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _logDirectory;
    private readonly Channel<WriteRequest> _queue;
    private readonly Task _writerLoop;

    // Per-day cache so a busy day's append loop does not re-read the file
    // from disk on every flush. The writer task is the only thread that
    // touches it, so no extra synchronization is needed.
    private readonly Dictionary<string, List<LogEntry>> _cachePerDay = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new logger writing to <paramref name="logDirectory"/>.
    /// The directory is created if it does not exist.
    /// </summary>
    /// <param name="logDirectory">Absolute or UNC path where daily files are stored.</param>
    /// <exception cref="ArgumentException">Thrown when the path is null or empty.</exception>
    public JsonDailyLogger(string logDirectory)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
        }

        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);

        _queue = Channel.CreateUnbounded<WriteRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _writerLoop = Task.Run(WriterLoopAsync);
    }

    /// <summary>
    /// Absolute path of the directory where daily log files are written.
    /// </summary>
    public string LogDirectory => _logDirectory;

    /// <inheritdoc />
    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Day file is decided here so an entry produced just before midnight
        // lands in the right file even if the writer task drains it later.
        string filePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.json");

        // Cahier asks for UNC paths in the log. Real UNC only exists for
        // network shares — for local paths we fall back to the Windows
        // extended-length prefix (\\?\). Copy the entry so we don't mutate
        // the caller's object.
        LogEntry normalized = new()
        {
            Timestamp = entry.Timestamp,
            JobName = entry.JobName,
            SourceFile = LogPathHelper.ToNormalizedPath(entry.SourceFile),
            TargetFile = LogPathHelper.ToNormalizedPath(entry.TargetFile),
            FileSize = entry.FileSize,
            FileTransferTimeMs = entry.FileTransferTimeMs,
            EncryptionTimeMs = entry.EncryptionTimeMs,
            EventType = entry.EventType,
        };

        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new WriteRequest(filePath, normalized, ack)))
        {
            // Queue is closed (post-Dispose). Mirrors the existing v1 contract
            // when the logger has been torn down: drop silently rather than
            // throw across what is usually a host-shutdown code path.
            return;
        }

        // Block until the writer task signals durability (or surfaces the
        // I/O error) — keeps Append's v1 sync contract.
        ack.Task.GetAwaiter().GetResult();
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // Drain everything currently queued so concurrent appends
                // collapse into a single file write per day file. Bounded by
                // what producers managed to enqueue between two iterations.
                var batch = new List<WriteRequest>();
                while (_queue.Reader.TryRead(out var req))
                {
                    batch.Add(req);
                }

                FlushBatch(batch);
            }
        }
        finally
        {
            // Path normal: TryComplete() makes WaitToReadAsync return false
            // only after the channel is drained, so this finally usually has
            // nothing left. Safety net for any future forced-cancellation /
            // unexpected exception path: route the survivors through
            // FlushBatch so callers either see their entry on disk or get a
            // surfaced exception — never a silent TrySetResult that would
            // claim durability for entries that never reached the file.
            var leftover = new List<WriteRequest>();
            while (_queue.Reader.TryRead(out var req)) leftover.Add(req);
            if (leftover.Count > 0)
            {
                try { FlushBatch(leftover); }
                catch (Exception ex)
                {
                    foreach (var req in leftover) req.Ack.TrySetException(ex);
                }
            }
        }
    }

    private void FlushBatch(List<WriteRequest> batch)
    {
        // Group by day file: a midnight rollover inside one batch would
        // otherwise mix entries across two files.
        foreach (var group in batch.GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            string filePath = group.Key;
            if (!_cachePerDay.TryGetValue(filePath, out var entries))
            {
                entries = ReadExisting(filePath);
                _cachePerDay[filePath] = entries;
            }

            int addedThisGroup = 0;
            foreach (var req in group)
            {
                entries.Add(req.Entry);
                addedThisGroup++;
            }

            try
            {
                WriteAtomic(filePath, entries);
            }
            catch (Exception ex)
            {
                // Roll back the in-memory cache so the next flush retries
                // the same entries cleanly, then surface the error to every
                // caller in this group.
                entries.RemoveRange(entries.Count - addedThisGroup, addedThisGroup);
                foreach (var req in group) req.Ack.TrySetException(ex);
                continue;
            }

            foreach (var req in group) req.Ack.TrySetResult();
        }
    }

    private static void WriteAtomic(string filePath, List<LogEntry> entries)
    {
        string tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(entries, SerializerOptions));
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception)
        {
            try { File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    private static List<LogEntry> ReadExisting(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new List<LogEntry>();
        }

        string raw = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<LogEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<LogEntry>>(raw) ?? new List<LogEntry>();
        }
        catch (JsonException ex)
        {
            // Preserve the corrupted file instead of overwriting it, so the
            // day's entries stay available for incident analysis.
            string backupPath = $"{filePath}.corrupted-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(filePath, backupPath);

            // Class-library diagnostic only. Mirrors XmlDailyLogger.ReadExisting
            // — host owns Console; library must not write to stderr.
            Trace.TraceWarning($"[EasyLog] Corrupted log file moved to {backupPath} - {ex.Message}");
            return new List<LogEntry>();
        }
    }

    /// <summary>
    /// Stops accepting new entries, waits for the writer task to drain any
    /// in-flight batch, and releases the channel. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Closing the writer side makes WaitToReadAsync return false once the
        // channel is empty, so the loop sorts after draining any in-flight
        // batch — preserving the durability contract for callers whose Append
        // returned successfully before Dispose.
        _queue.Writer.TryComplete();

        try { _writerLoop.GetAwaiter().GetResult(); }
        catch (AggregateException) { /* surfaced through individual ack TCS */ }
    }

    private sealed record WriteRequest(string FilePath, LogEntry Entry, TaskCompletionSource Ack);
}
