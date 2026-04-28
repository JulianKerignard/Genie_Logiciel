using System.Collections.Concurrent;
using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services;

// Singleton that tracks the real-time state of every backup job.
// Persists a JSON array to AppConfig.Instance.StateFilePath (rewritten atomically) and
// exposes a per-job INotifyPropertyChanged view + a JobProgressChanged event so a GUI
// can react to live updates without re-reading state.json.
public sealed class StateTracker
{
    private static readonly Lazy<StateTracker> _instance = new(() => new StateTracker());
    public static StateTracker Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, JobProgress> _jobs = new();

    // Raised after every Update with the snapshot just persisted.
    // Subscribers receive the immutable StateEntry for inspection / logging.
    public event EventHandler<StateEntry>? JobProgressChanged;

    // Live observable views, indexed by job name. The same instance is reused across updates
    // so GUI bindings stay attached for the lifetime of the job.
    public IReadOnlyDictionary<string, JobProgress> Jobs => _jobs;

    private StateTracker() { }

    // Inserts or replaces the snapshot for a job, persists the full state.json atomically,
    // mutates the matching JobProgress to fire INotifyPropertyChanged, then raises JobProgressChanged.
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

        var progress = _jobs.GetOrAdd(entry.Name, name => new JobProgress(name));
        progress.CurrentFile = entry.CurrentSource;
        progress.FilesRemaining = entry.FilesRemaining;
        progress.Percent = entry.Progress;

        var handler = JobProgressChanged;
        handler?.Invoke(this, entry);
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
            FileHelpers.QuarantineCorruptedFile(path, ex, "StateTracker");
            return new List<StateEntry>();
        }
        catch (IOException)
        {
            // Transient read error (file locked by another process): treat as empty
            // without quarantining, so the next Update rewrites the file.
            return new List<StateEntry>();
        }
    }
}
