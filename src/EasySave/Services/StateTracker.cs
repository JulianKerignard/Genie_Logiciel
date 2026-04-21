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

            FileHelpers.WriteAllTextAtomic(path, JsonSerializer.Serialize(states, FileHelpers.IndentedJsonOptions));
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
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Treat a corrupted or transiently unreadable state file as empty
            // rather than crashing the caller inside the lock.
            return new List<StateEntry>();
        }
    }
}
