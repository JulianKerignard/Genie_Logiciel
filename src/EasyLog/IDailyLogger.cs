namespace EasyLog;

/// <summary>
/// Appends log entries to a daily log file.
/// Designed to be reusable across multiple ProSoft applications.
/// Implementations must be thread-safe and must preserve the v1.0
/// contract for backward compatibility with existing consumers.
/// </summary>
public interface IDailyLogger
{
    /// <summary>
    /// Appends a single log entry to the current day log file.
    /// A new file is created automatically when the date changes.
    /// </summary>
    /// <param name="entry">The log entry to append. Must not be null.</param>
    void Append(LogEntry entry);
}
