using System.Diagnostics;
using System.Threading.Channels;
using System.Xml;
using System.Xml.Linq;

namespace EasyLog;

/// <summary>
/// Writes <see cref="LogEntry"/> instances to a daily XML file
/// named "yyyy-MM-dd.xml" inside a configurable directory.
/// Each file is a valid document with a <c>&lt;Logs&gt;</c> root element and
/// one <c>&lt;Entry&gt;</c> child per logged operation, conforming to the
/// schema in <c>EasyLog.Schemas.easysave-log.xsd</c>.
/// </summary>
/// <remarks>
/// <para>
/// Concurrent <see cref="Append"/> calls are funneled through an in-memory
/// <see cref="Channel{T}"/> consumed by a single writer task — same pattern
/// as <see cref="JsonDailyLogger"/>. The writer drains everything currently
/// queued, groups by day file, mutates a per-day in-memory <see cref="XDocument"/>
/// cache, and writes the file atomically. So 4 jobs publishing 1000 entries
/// concurrently triggers a handful of file writes instead of 4000 reparse-
/// rewrite cycles. The previous implementation reloaded + re-serialized the
/// full DOM under a global lock at every Append, producing O(n²) behaviour
/// on long-running jobs.
/// </para>
/// <para>
/// <see cref="Append"/> stays <c>void</c> and synchronous: each call blocks
/// until its entry has been flushed (or the writer reports an error), so the
/// v1.0 durability contract is preserved — no log loss on crash between
/// Append return and the next backup step.
/// </para>
/// </remarks>
public sealed class XmlDailyLogger : IDailyLogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly XmlFormatter _formatter = new();
    private readonly Channel<WriteRequest> _queue;
    private readonly Task _writerLoop;
    private readonly ILogShipper? _shipper;
    private readonly LogMode _mode;

    // Per-day cache of the live XDocument so a busy day does not re-parse
    // the file from disk on every flush. The writer task is the only thread
    // that touches it, so no extra synchronization is needed.
    private readonly Dictionary<string, XDocument> _cachePerDay = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new logger writing to <paramref name="logDirectory"/>.
    /// The directory is created if it does not exist.
    /// </summary>
    /// <param name="logDirectory">Absolute or UNC path where daily XML files are stored.</param>
    /// <param name="shipper">Optional V3 centralized shipper. See <see cref="JsonDailyLogger"/> for the contract.</param>
    /// <param name="mode">Routing for <see cref="Append"/> calls. See <see cref="JsonDailyLogger"/> for the contract.</param>
    /// <exception cref="ArgumentException">Thrown when the path is null or empty.</exception>
    public XmlDailyLogger(string logDirectory, ILogShipper? shipper = null, LogMode mode = LogMode.Local)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
            throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
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

    /// <summary>Absolute path of the directory where daily log files are written.</summary>
    public string LogDirectory => _logDirectory;

    /// <inheritdoc />
    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Day file decided here so an entry produced just before midnight
        // lands in the right file even if the writer task drains it later.
        string filePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.xml");
        LogEntry normalized = LogRouter.Normalize(entry);

        if (LogRouter.ShouldShip(_mode))
        {
            _shipper!.Append(normalized);
        }

        if (!LogRouter.ShouldWriteLocal(_mode))
        {
            return;
        }

        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new WriteRequest(filePath, normalized, ack)))
        {
            // Queue is closed (post-Dispose). Same convention as JsonDailyLogger:
            // drop silently rather than throw across host-shutdown code paths.
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
                    // Belt-and-braces — FlushBatch swallows per-group failures
                    // itself; any future throw escaping it would otherwise
                    // kill the writer and hang every Append on its TCS.
                    foreach (var req in batch) req.Ack.TrySetException(ex);
                }
            }
        }
        finally
        {
            // Safety net for any forced-cancellation / unexpected exception
            // path: route survivors through FlushBatch so callers either see
            // their entry on disk or get a surfaced exception — never a
            // silent TrySetResult that would claim durability for entries
            // that never reached the file.
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

            int addedThisGroup = 0;
            XDocument? doc = null;
            try
            {
                if (!_cachePerDay.TryGetValue(filePath, out doc))
                {
                    doc = ReadExisting(filePath);
                    _cachePerDay[filePath] = doc;
                }

                foreach (var req in group)
                {
                    doc.Root!.Add(XElement.Parse(_formatter.Format(req.Entry)));
                    addedThisGroup++;
                }

                WriteAtomic(filePath, doc);
                foreach (var req in group) req.Ack.TrySetResult();
            }
            catch (Exception ex)
            {
                // Roll back the in-memory cache so the next flush retries
                // the same entries cleanly. addedThisGroup is 0 when the
                // throw came from ReadExisting (doc was never mutated)
                // or when doc is still null (initial assignment failed).
                if (doc?.Root is not null && addedThisGroup > 0)
                {
                    var children = doc.Root.Elements().ToList();
                    int removeFrom = children.Count - addedThisGroup;
                    for (int i = children.Count - 1; i >= removeFrom; i--)
                    {
                        children[i].Remove();
                    }
                }
                foreach (var req in group) req.Ack.TrySetException(ex);
            }
        }
    }

    private static XDocument ReadExisting(string filePath)
    {
        if (!File.Exists(filePath))
            return new XDocument(new XElement("Logs"));

        try
        {
            return XDocument.Load(filePath);
        }
        catch (XmlException ex)
        {
            // Genuinely malformed XML — preserve the file as evidence and start fresh.
            string backupPath = $"{filePath}.corrupted-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(filePath, backupPath);
            Trace.TraceWarning($"[EasyLog] Corrupted XML log moved to {backupPath} - {ex.Message}");
            return new XDocument(new XElement("Logs"));
        }
        // IOException / UnauthorizedAccessException intentionally propagated.
        // A transient lock (antivirus, OneDrive sync, log viewer holding the file
        // briefly) used to flow through the same arm and quarantine the live
        // daily file, splitting the day's entries across multiple files. The
        // convention now matches JsonDailyLogger and StateTracker / JobRepository
        // / SettingsRepository: only the format-specific parse exception triggers
        // quarantine; IO failures bubble up and the caller decides.
    }

    private static void WriteAtomic(string filePath, XDocument doc)
    {
        string tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            doc.Save(tmpPath);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception)
        {
            try { File.Delete(tmpPath); } catch { }
            throw;
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

        _queue.Writer.TryComplete();

        try { _writerLoop.GetAwaiter().GetResult(); }
        catch (AggregateException) { /* surfaced through individual ack TCS */ }
    }

    private sealed record WriteRequest(string FilePath, LogEntry Entry, TaskCompletionSource Ack);
}
