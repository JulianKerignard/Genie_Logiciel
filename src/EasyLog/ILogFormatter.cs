namespace EasyLog;

/// <summary>
/// Serializes a <see cref="LogEntry"/> to the textual representation used in
/// daily log files. Concrete formatters are introduced in EasyLog v2 to support
/// both JSON and XML outputs without changing the v1 <see cref="IDailyLogger"/>
/// contract.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe and pure: <see cref="Format"/> may be
/// called from concurrent backup jobs and must not mutate shared state.
/// </remarks>
public interface ILogFormatter
{
    /// <summary>
    /// Returns the serialized representation of <paramref name="entry"/>.
    /// </summary>
    /// <param name="entry">The log entry to serialize. Must not be null.</param>
    /// <returns>The serialized text, ready to append to the daily log file.</returns>
    string Format(LogEntry entry);

    /// <summary>
    /// File extension (including the leading dot) used by daily log files
    /// produced by this formatter, e.g. <c>".json"</c> or <c>".xml"</c>.
    /// </summary>
    string FileExtension { get; }
}
