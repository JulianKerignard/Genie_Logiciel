using System.Text.Json;

namespace EasyLog;

/// <summary>
/// Writes <see cref="LogEntry"/> instances to a daily JSON file
/// named "yyyy-MM-dd.json" inside a configurable directory.
/// Each file contains a JSON array of entries appended over the day.
/// Writes are serialized with a lock to stay safe under concurrent calls.
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

        string filePath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.json");

        lock (_writeLock)
        {
            List<LogEntry> entries = ReadExisting(filePath);
            entries.Add(entry);
            File.WriteAllText(filePath, JsonSerializer.Serialize(entries, SerializerOptions));
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
        catch (JsonException)
        {
            // File exists but is corrupted; start a fresh list rather than crashing the backup.
            return new List<LogEntry>();
        }
    }
}
