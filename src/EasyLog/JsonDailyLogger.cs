using System.Diagnostics;
using System.Text.Json;

namespace EasyLog;

/// <summary>
/// Writes <see cref="LogEntry"/> instances to a daily JSON file
/// named "yyyy-MM-dd.json" inside a configurable directory.
/// Each file contains a JSON array of entries appended over the day.
/// Writes are serialized with a lock to stay safe under concurrent calls
/// and performed atomically via a temporary file to avoid partial writes.
/// Note: each append rewrites the whole day file (O(n) on the daily count);
/// this is acceptable for v1.0 volumes and can be revisited later if needed.
/// </summary>
public sealed class JsonDailyLogger : IDailyLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _logDirectory;
    private readonly object _writeLock = new();

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
    }

    /// <summary>
    /// Absolute path of the directory where daily log files are written.
    /// </summary>
    public string LogDirectory => _logDirectory;

    /// <inheritdoc />
    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Local date is intentional: daily files must align with the business day
        // seen by the operator, not UTC.
        string filePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.json");

        lock (_writeLock)
        {
            List<LogEntry> entries = ReadExisting(filePath);
            entries.Add(entry);
            WriteAtomic(filePath, entries);
        }
    }

    private static void WriteAtomic(string filePath, List<LogEntry> entries)
    {
        // Write to a side file first, then move it over the target path.
        // A same-volume File.Move with overwrite is atomic on NTFS and POSIX,
        // so a process kill mid-write never leaves a half-written log.
        string tmpPath = filePath + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(entries, SerializerOptions));
        File.Move(tmpPath, filePath, overwrite: true);
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
            // Corrupted file: surface a warning so an incident can be diagnosed,
            // then start fresh rather than crashing the backup job.
            Trace.TraceWarning($"[EasyLog] Corrupted log file discarded: {filePath} - {ex.Message}");
            return new List<LogEntry>();
        }
    }
}
