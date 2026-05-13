namespace EasyLog;

/// <summary>
/// Forwards log entries to a centralized collector. Implementations must:
/// (1) accept entries without blocking the caller — a transient outage on
/// the collector side never stalls a backup job; (2) buffer locally while
/// the network is down and flush on reconnect; (3) preserve entry order
/// per producer thread. Drop semantics are implementation-defined but
/// must be documented.
/// </summary>
public interface ILogShipper : IAsyncDisposable
{
    /// <summary>
    /// Enqueues <paramref name="entry"/> for shipment. Returns synchronously
    /// without performing any network I/O — the actual POST is handled by a
    /// background task. Implementations may reject the entry (return value
    /// or exception) once <see cref="IAsyncDisposable.DisposeAsync"/> has
    /// been initiated.
    /// </summary>
    /// <param name="entry">Entry to ship. Must not be null.</param>
    void Append(LogEntry entry);
}
