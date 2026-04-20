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

        // Cahier asks for UNC paths in the log. Real UNC only exists for
        // network shares — for local paths we fall back to the Windows
        // extended-length prefix (\\?\), which is the closest portable
        // equivalent. Copy the entry so we don't mutate the caller's object.
        LogEntry normalized = new()
        {
            Timestamp = entry.Timestamp,
            JobName = entry.JobName,
            SourceFile = ToNormalizedPath(entry.SourceFile),
            TargetFile = ToNormalizedPath(entry.TargetFile),
            FileSize = entry.FileSize,
            FileTransferTimeMs = entry.FileTransferTimeMs,
        };

        lock (_writeLock)
        {
            List<LogEntry> entries = ReadExisting(filePath);
            entries.Add(normalized);
            WriteAtomic(filePath, entries);
        }
    }

    private static string ToNormalizedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        // Already a \\-prefixed path (real UNC network share or extended-length),
        // leave it alone.
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;

        string full = Path.GetFullPath(path);

        // On Windows, wrap a local drive path with the extended-length prefix.
        // On Unix there's no equivalent, just return the absolute path.
        if (OperatingSystem.IsWindows() && full.Length > 1 && full[1] == ':')
        {
            return @"\\?\" + full;
        }

        return full;
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

            Trace.TraceWarning($"[EasyLog] Corrupted log file moved to {backupPath} - {ex.Message}");
            Console.Error.WriteLine($"[EasyLog] Corrupted log file moved to {backupPath} ({ex.Message})");
            return new List<LogEntry>();
        }
    }
}
