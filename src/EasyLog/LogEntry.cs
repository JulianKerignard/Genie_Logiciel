using System.Text.Json.Serialization;

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

    /// <summary>
    /// Encryption duration in milliseconds (EasyLog v2+).
    /// A negative value indicates an encryption failure.
    /// Null when no encryption was performed (default for v1 consumers).
    /// </summary>
    /// <remarks>
    /// The property is omitted from the JSON output when null so that v1
    /// consumers reading the log file see the exact same shape as before.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EncryptionTimeMs { get; set; }

    /// <summary>
    /// V3 event category for this log row (EasyLog v3+). Null on v1 / v2 file-
    /// transfer rows so existing consumers see the exact JSON / XML shape they
    /// always have. Serialized as the enum member name (e.g. "BigFileEnqueued"),
    /// not the numeric value, so the daily log stays human-readable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LogEvent? EventType { get; set; }

    /// <summary>
    /// Host that produced the entry (EasyLog v1.2+). Lets a centralized
    /// collector demultiplex entries coming from multiple workstations into
    /// a single daily file. Null on v1 / v2 logs and on v3+ entries that
    /// the producer chose not to stamp — the daily-file readers must treat
    /// the field as optional.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineName { get; set; }

    /// <summary>
    /// User account that produced the entry (EasyLog v1.2+). Same retro-
    /// compat contract as <see cref="MachineName"/>: optional, null on
    /// pre-v3 logs, never required by readers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserName { get; set; }
}
