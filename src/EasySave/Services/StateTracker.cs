using System.Text.Json;

namespace EasySave.Services;

// Singleton that tracks the real-time state of every backup job.
// State is persisted to AppConfig.Instance.StateFilePath as a single JSON array,
// rewritten atomically on each call to Update.
public sealed class StateTracker
{
    // Lazy, thread-safe singleton instance.
    private static readonly Lazy<StateTracker> _instance = new(() => new StateTracker());
    public static StateTracker Instance => _instance.Value;

    // Serialises concurrent writes coming from multiple backup managers.
    private readonly object _lock = new();

    private StateTracker() { }

    // Inserts or replaces the snapshot for a job, then rewrites the state file atomically.
    public void Update(StateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            var path = AppConfig.Instance.StateFilePath;
            FileHelpers.EnsureDirectoryExists(path);

            var states = ReadCurrentEntries(path);
            states.RemoveAll(s => s.Name == entry.Name);
            states.Add(entry);

            WriteAtomically(path, states);
        }
    }

    private static List<StateEntry> ReadCurrentEntries(string path)
    {
        if (!File.Exists(path))
        {
            return new List<StateEntry>();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<StateEntry>>(json) ?? new List<StateEntry>();
        }
        catch (JsonException ex)
        {
            QuarantineCorruptedFile(path, ex);
            return new List<StateEntry>();
        }
        catch (IOException)
        {
            // Transient read error (file locked by another process): treat as empty
            // without quarantining, so the next Update rewrites the file.
            return new List<StateEntry>();
        }
    }

    // Renames a corrupted state file so operators can inspect it later,
    // instead of silently wiping all job states on the next Update.
    private static void QuarantineCorruptedFile(string path, Exception reason)
    {
        try
        {
            var quarantinePath = $"{path}.corrupted-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            File.Move(path, quarantinePath);
            Console.Error.WriteLine(
                $"[StateTracker] {Path.GetFileName(path)} was unreadable and has been moved to " +
                $"{Path.GetFileName(quarantinePath)}. Reason: {reason.Message}");
        }
        catch
        {
            // If the rename itself fails, fall back to returning an empty list so the app keeps running.
        }
    }

    private static void WriteAtomically(string path, List<StateEntry> states)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(states, FileHelpers.IndentedJsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
