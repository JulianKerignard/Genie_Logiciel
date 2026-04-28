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
    // Subscribers receive the StateEntry passed to Update — treat it as a snapshot
    // and do not mutate; StateEntry currently has public setters but is used as a DTO.
    public event EventHandler<StateEntry>? JobProgressChanged;

    // Live observable views, indexed by job name. The same instance is reused across updates
    // so GUI bindings stay attached for the lifetime of a job.
    // Entries are never auto-removed: finished jobs stay in the map so the GUI keeps showing
    // their final progress until the consumer explicitly chooses to filter or evict them.
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

        // Observable side-effects run outside _lock on purpose: PropertyChanged subscribers
        // (Avalonia bindings, log forwarders) may call back into Update. Keeping them under
        // _lock would deadlock or violate the lock invariant on re-entry.
        // The trade-off is eventually-consistent JobProgress (state.json is always consistent;
        // the in-memory observable can briefly see interleaved values under concurrent updates
        // for the same job — acceptable for a transient progress display).
        var progress = _jobs.GetOrAdd(entry.Name, name => new JobProgress(name));
        progress.CurrentFile = entry.CurrentSource;
        progress.FilesRemaining = entry.FilesRemaining;
        progress.Percent = entry.Progress;

        JobProgressChanged?.Invoke(this, entry);
    }

    // Drops the state entry for the given job name (case-insensitive) and rewrites state.json.
    // No-op if no entry matches. Also evicts the matching JobProgress observable so the GUI
    // stops binding to a deleted job.
    public void Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_lock)
        {
            var path = AppConfig.Instance.StateFilePath;
            if (File.Exists(path))
            {
                var states = ReadCurrentEntries(path);
                var initialCount = states.Count;
                states.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

                if (states.Count != initialCount)
                {
                    FileHelpers.EnsureDirectoryExists(path);
                    FileHelpers.WriteAllTextAtomic(path, JsonSerializer.Serialize(states, FileHelpers.IndentedJsonOptions));
                }
            }
        }

        // Outside the lock — same reasoning as Update for observable side-effects.
        // Case-insensitive lookup so an entry added under a different casing is still evicted.
        var match = _jobs.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _jobs.TryRemove(match, out _);
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
