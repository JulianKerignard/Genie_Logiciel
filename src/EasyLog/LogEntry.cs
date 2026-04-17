namespace EasyLog;

/// <summary>
/// Represents a single operation recorded during a backup job.
/// The field set matches the minimum contract required by EasySave v1.0
/// and must remain backward compatible with future revisions.
/// </summary>
public sealed class LogEntry
{
    /// <summary>
    /// ISO-8601 timestamp of the logged action.
    /// </summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>
    /// Name of the backup job that produced this entry.
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Full source path in UNC format (e.g. \\server\share\file.txt).
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// Full target path in UNC format.
    /// </summary>
    public string TargetFile { get; set; } = string.Empty;

    /// <summary>
    /// Size of the source file in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// File transfer duration in milliseconds.
    /// A negative value indicates a transfer error.
    /// </summary>
    public long FileTransferTimeMs { get; set; }
}
