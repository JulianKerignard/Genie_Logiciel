using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services;

// Singleton that tracks the real-time state of every backup job.
// Persists a JSON array to AppConfig.Instance.StateFilePath (rewritten atomically) and
// exposes a per-job INotifyPropertyChanged view + a JobProgressChanged event so a GUI
// can react to live updates without re-reading state.json.
//
// V3 write path (UpdateJob): updates the in-memory cache immediately and schedules a
// throttled disk flush (≤200 ms). The V2 write path (Update/Pause/Resume/Remove) still
// flushes synchronously so monitoring tools always see a consistent file.
public sealed class StateTracker : IStateRepository
{
    private static readonly Lazy<StateTracker> _instance = new(() => new StateTracker());
    public static StateTracker Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, JobProgress> _jobs = new();

    // In-memory cache — single source of truth for both V2 and V3 write paths.
    // Guarded by _lock. Loaded lazily on first access; invalidated automatically
    // when AppConfig.Instance.StateFilePath changes (test isolation).
    private readonly Dictionary<string, StateEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string _cachedPath = string.Empty;
    private bool _cacheDirty;

    // Throttles disk writes issued by UpdateJob: at most one write per 200 ms.
    private readonly Timer _flushTimer;

    // Raised after every Update with the snapshot just persisted.
    // Subscribers receive the StateEntry passed to Update — treat it as a snapshot
    // and do not mutate; StateEntry currently has public setters but is used as a DTO.
    public event EventHandler<StateEntry>? JobProgressChanged;

    // Live observable views, indexed by job name. The same instance is reused across updates
    // so GUI bindings stay attached for the lifetime of a job.
    // Entries are never auto-removed: finished jobs stay in the map so the GUI keeps showing
    // their final progress until the consumer explicitly chooses to filter or evict them.
    public IReadOnlyDictionary<string, JobProgress> Jobs => _jobs;

    private StateTracker()
    {
        _flushTimer = new Timer(_ => FlushFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    // Inserts or replaces the snapshot for a job, persists the full state.json atomically,
    // mutates the matching JobProgress to fire INotifyPropertyChanged, then raises JobProgressChanged.
    public void Update(StateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            EnsureCacheLoaded();
            _cache[entry.Name] = entry;
            FlushCacheUnderLock();
        }

        // Observable side-effects run outside _lock on purpose: PropertyChanged subscribers
        // (Avalonia bindings, log forwarders) may call back into Update. Keeping them under
        // _lock would deadlock or violate the lock invariant on re-entry.
        var progress = _jobs.GetOrAdd(entry.Name, name => new JobProgress(name));
        progress.CurrentFile = entry.CurrentSource;
        progress.FilesRemaining = entry.FilesRemaining;
        progress.Percent = entry.Progress;

        JobProgressChanged?.Invoke(this, entry);
    }

    // Marks the matching job as Paused and persists the human-readable reason (e.g.
    // "BusinessSoftwareDetected: calc.exe"). No-op if no entry matches the given name.
    // Resume restores the previous JobState (typically Active) and clears the reason.
    public void Pause(string name, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        // Only an Active job can be paused — pausing an Inactive (finished) job would
        // mark it Paused in state.json and confuse any monitoring tool reading the file.
        TransitionState(name, prev => prev == JobState.Active ? JobState.Paused : prev, reason);
    }

    public void Resume(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TransitionState(name, prev => prev == JobState.Paused ? JobState.Active : prev, string.Empty);
    }

    private void TransitionState(string name, Func<JobState, JobState> nextState, string reason)
    {
        StateEntry? snapshot = null;

        lock (_lock)
        {
            EnsureCacheLoaded();

            if (!_cache.TryGetValue(name, out var entry)) return;

            var next = nextState(entry.State);
            // No state transition means the call is rejected (e.g. Pause on Inactive,
            // Resume on Active): leave the file and the reason untouched, no event.
            if (entry.State == next) return;

            entry.State = next;
            entry.PauseReason = reason;
            entry.LastActionTime = DateTimeOffset.Now;

            FlushCacheUnderLock();
            snapshot = entry;
        }

        if (snapshot is not null)
        {
            JobProgressChanged?.Invoke(this, snapshot);
        }
    }

    // Returns the current persisted StateEntry for the given job name, or null if not found.
    // Used by the UI adapter to determine the resume index after a pause.
    public StateEntry? GetState(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_lock)
        {
            EnsureCacheLoaded();
            return _cache.TryGetValue(name, out var e) ? e : null;
        }
    }

    // Drops the state entry for the given job name (case-insensitive) and rewrites state.json.
    // No-op if no entry matches. Also evicts the matching JobProgress observable so the GUI
    // stops binding to a deleted job.
    public void Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_lock)
        {
            EnsureCacheLoaded();
            if (_cache.Remove(name))
            {
                FlushCacheUnderLock();
            }
        }

        // Outside the lock — same reasoning as Update for observable side-effects.
        var match = _jobs.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _jobs.TryRemove(match, out _);
        }
    }

    // IStateRepository — V3 fast path. Updates the in-memory cache immediately and schedules
    // a throttled disk flush (≤200 ms). Also propagates the observable event so Avalonia
    // bindings react without waiting for the flush.
    public void UpdateJob(string name, JobState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        StateEntry snapshot;
        lock (_lock)
        {
            EnsureCacheLoaded();
            if (!_cache.TryGetValue(name, out var entry))
            {
                entry = new StateEntry { Name = name };
                _cache[name] = entry;
            }
            entry.State = state;
            entry.LastActionTime = DateTimeOffset.Now;
            _cacheDirty = true;
            snapshot = entry;
        }

        // Schedule a throttled flush; observable side-effects run outside _lock.
        _flushTimer.Change(200, Timeout.Infinite);
        var progress = _jobs.GetOrAdd(name, n => new JobProgress(n));
        JobProgressChanged?.Invoke(this, snapshot);
    }

    // IStateRepository
    public StateEntry? GetJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_lock)
        {
            EnsureCacheLoaded();
            return _cache.TryGetValue(name, out var e) ? e : null;
        }
    }

    // IStateRepository — Callers must not mutate the returned entries.
    public IReadOnlyList<StateEntry> GetAll()
    {
        lock (_lock)
        {
            EnsureCacheLoaded();
            return _cache.Values.ToList();
        }
    }

    // IStateRepository
    public void RemoveJob(string name) => Remove(name);

    // Forces an immediate cache flush — intended for tests and graceful shutdown.
    public void FlushNow()
    {
        lock (_lock)
        {
            if (!_cacheDirty) return;
            FlushCacheUnderLock();
        }
    }

    // Populates _cache from disk when accessed for the first time, or when
    // AppConfig.Instance.StateFilePath changes (handles test isolation automatically).
    private void EnsureCacheLoaded()
    {
        var path = AppConfig.Instance.StateFilePath;
        if (path == _cachedPath) return;

        _cache.Clear();
        _cacheDirty = false;
        _cachedPath = path;

        foreach (var e in ReadCurrentEntries(path))
            _cache[e.Name] = e;
    }

    private void FlushCacheUnderLock()
    {
        var path = AppConfig.Instance.StateFilePath;
        FileHelpers.EnsureDirectoryExists(path);
        FileHelpers.WriteAllTextAtomic(path, JsonSerializer.Serialize(
            _cache.Values.ToList(), FileHelpers.IndentedJsonOptions));
        // Only mark clean after a successful write — a failed write must leave
        // _cacheDirty true so FlushNow() and the next timer tick can retry.
        _cacheDirty = false;
    }

    private void FlushFromTimer()
    {
        // Timer callbacks run on a thread-pool thread; an unhandled exception
        // there terminates the process on .NET 8. Catch and log so a transient
        // state.json lock (AV scan, OneDrive, network share momentary drop)
        // degrades gracefully — _cacheDirty stays true and the next UpdateJob
        // call re-arms the timer for a retry.
        try
        {
            lock (_lock)
            {
                if (!_cacheDirty) return;
                FlushCacheUnderLock();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("[StateTracker] Throttled flush failed: {0}. Cache remains dirty — next UpdateJob or FlushNow will retry.", ex.Message);
        }
    }

    // Transient IOException is propagated to the caller (Update / Remove): swallowing it
    // and returning [] would cause the next atomic write to overwrite state.json with only
    // the current job's entry, wiping every other job's live state (issue #69).
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
    }
}
