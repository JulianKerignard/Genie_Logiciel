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
    private readonly ILogShipper? _shipper;
    private readonly LogMode _mode;

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
    /// <param name="shipper">
    /// Optional V3 centralized shipper. When <paramref name="mode"/> is
    /// <see cref="LogMode.Centralized"/> or <see cref="LogMode.Both"/>, every
    /// <see cref="Append"/> also enqueues the entry on this shipper. Null
    /// (default) keeps the v1/v2 local-only behaviour.
    /// </param>
    /// <param name="mode">
    /// Routing for <see cref="Append"/> calls. <see cref="LogMode.Local"/>
    /// (default) writes the daily file only; <see cref="LogMode.Centralized"/>
    /// skips the local file and only ships; <see cref="LogMode.Both"/> does both.
    /// When <paramref name="shipper"/> is null, the mode is forced back to
    /// <see cref="LogMode.Local"/> so a misconfigured centralized setup
    /// never causes silent log loss.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when the path is null or empty.</exception>
    public JsonDailyLogger(string logDirectory, ILogShipper? shipper = null, LogMode mode = LogMode.Local)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
        }

        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        _shipper = shipper;
        _mode = LogRouter.Effective(shipper, mode);

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

        // Day file decided here so an entry produced just before midnight
        // lands in the right file even if the writer task drains it later.
        string filePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.json");
        LogEntry normalized = LogRouter.Normalize(entry);

        if (LogRouter.ShouldShip(_mode))
        {
            _shipper!.Append(normalized);
        }

        // Centralized-only mode: the shipper owns durability, skip the
        // local file. Operators run LogMode.Both during cut-over so a
        // host crash between Append and successful POST can fall back
        // to the local copy.
        if (!LogRouter.ShouldWriteLocal(_mode))
        {
            return;
        }

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

                try
                {
                    FlushBatch(batch);
                }
                catch (Exception ex)
                {
                    // Belt-and-braces: FlushBatch swallows per-group failures
                    // itself, but any future throw escaping it would kill the
                    // writer and hang every Append on its
                    // TaskCompletionSource. Surface to surviving acks.
                    foreach (var req in batch)
                    {
                        req.Ack.TrySetException(ex);
                    }
                }
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

            // Wrap the whole per-group body: a throw from ReadExisting
            // (transient IOException from AV / OneDrive on the first
            // append of the day) must NOT escape — it would kill the
            // writer task and hang every future Append on its
            // TaskCompletionSource.
            int addedThisGroup = 0;
            List<LogEntry>? entries = null;
            try
            {
                if (!_cachePerDay.TryGetValue(filePath, out entries))
                {
                    entries = ReadExisting(filePath);
                    _cachePerDay[filePath] = entries;
                }

                foreach (var req in group)
                {
                    entries.Add(req.Entry);
                    addedThisGroup++;
                }

                WriteAtomic(filePath, entries);
                foreach (var req in group) req.Ack.TrySetResult();
            }
            catch (Exception ex)
            {
                // Roll back the in-memory cache so the next flush retries
                // the same entries cleanly. addedThisGroup is 0 when the
                // throw came from ReadExisting (entries was never mutated)
                // or when entries is still null (initial assignment failed).
                if (entries is not null && addedThisGroup > 0)
                {
                    entries.RemoveRange(entries.Count - addedThisGroup, addedThisGroup);
                }
                // Surface the failure to every caller in this group and let
                // the next foreach iteration handle the next day file —
                // the writer task stays alive.
                foreach (var req in group) req.Ack.TrySetException(ex);
            }
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

        // GetAwaiter().GetResult() unwraps AggregateException, so a writer-task
        // fault reaches the catch site as the inner exception. Catch Exception,
        // not AggregateException, so Dispose actually swallows everything as
        // intended. Per-entry failures still surface to callers via ack TCS;
        // this catch only protects shutdown from a writer that escaped the
        // FlushBatch try/catch (e.g. an OOM in the channel reader path).
        try { _writerLoop.GetAwaiter().GetResult(); }
        catch (Exception) { /* surfaced through individual ack TCS */ }
    }

    private sealed record WriteRequest(string FilePath, LogEntry Entry, TaskCompletionSource Ack);
}
