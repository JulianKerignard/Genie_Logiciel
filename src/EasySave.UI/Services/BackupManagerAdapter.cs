using System.Text.Json;
using EasySave;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.UI.Services;

// Wraps the synchronous BackupManager to expose an async + pause/resume surface
// to the UI layer. Progress is tracked by polling state.json at 300 ms intervals.
public sealed class BackupManagerAdapter : IBackupManagerAdapter
{
    private readonly BackupManager _manager;
    private readonly object _lock = new();

    // Active runs: jobName → its CancellationTokenSource.
    private readonly Dictionary<string, CancellationTokenSource> _running = new();

    // Jobs paused via PauseJob that have not yet been resumed.
    // Preserved across the RunJobAsync finally so ResumeJob knows which jobs to restart.
    private readonly HashSet<string> _pausedByUs = new();

    // Reason stored per paused job so StateTracker.Pause can record it.
    private readonly Dictionary<string, string> _pauseReasons = new();

    private System.Threading.Timer? _pollTimer;
    private bool _disposed;

    public event EventHandler<StateEntry>? StateUpdated;

    public BackupManagerAdapter(BackupManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    public IReadOnlyList<BackupJob> GetJobs() => _manager.ListJobs();
    public void AddJob(BackupJob job) => _manager.AddJob(job);
    public void RemoveJob(string name) => _manager.RemoveJob(name);

    public bool IsJobRunning(string jobName)
    {
        lock (_lock) return _running.ContainsKey(jobName);
    }

    public async Task RunJobAsync(string jobName, int startFromIndex = 0, CancellationToken ct = default)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_running.ContainsKey(jobName)) return;
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _running[jobName] = cts;
            _pausedByUs.Remove(jobName);
        }
        EnsurePolling();
        try
        {
            // ExecuteJob is synchronous; run on thread pool and pass the linked token
            // so cancel requests stop the job at the next file boundary.
            await Task.Run(() => _manager.ExecuteJob(jobName, startFromIndex, cts.Token), cts.Token)
                      .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Use _pausedByUs membership, not cts.IsCancellationRequested, to decide
            // whether this was an intentional pause. An external CancellationToken
            // (e.g. app shutdown) also sets IsCancellationRequested but must NOT
            // leave the job in state.json as Paused with no resume path.
            bool cancelledByPause;
            string? pauseReason = null;

            lock (_lock)
            {
                cancelledByPause = _pausedByUs.Contains(jobName);
                _running.Remove(jobName);
                if (!cancelledByPause)
                {
                    _pausedByUs.Remove(jobName);
                }
                else
                {
                    _pauseReasons.TryGetValue(jobName, out pauseReason);
                    _pauseReasons.Remove(jobName);
                }
                cts.Dispose();
            }

            // Persist the pause reason to state.json so monitoring tools and the UI
            // can display the correct status without polling.
            if (cancelledByPause && pauseReason is not null)
            {
                StateTracker.Instance.Pause(jobName, pauseReason);
            }

            if (!HasRunningJobs()) StopPolling();
        }
    }

    public void PauseJob(string jobName, string reason = "UserRequested")
    {
        lock (_lock)
        {
            if (_running.TryGetValue(jobName, out var cts))
            {
                _pausedByUs.Add(jobName);
                _pauseReasons[jobName] = reason;
                cts.Cancel();
            }
        }
    }

    public void ResumeJob(string jobName)
    {
        bool wasPaused;
        lock (_lock) wasPaused = _pausedByUs.Remove(jobName);
        if (!wasPaused) return;

        // Compute the resume index from the last persisted state so a Full backup
        // continues from where it stopped rather than re-copying everything.
        int startFromIndex = 0;
        var state = StateTracker.Instance.GetState(jobName);
        if (state is { TotalFilesEligible: > 0 })
            startFromIndex = state.TotalFilesEligible - state.FilesRemaining;

        // Clear the paused marker so state.json shows Active again when the job starts.
        StateTracker.Instance.Resume(jobName);

        _ = RunJobAsync(jobName, startFromIndex);
    }

    private bool HasRunningJobs() { lock (_lock) return _running.Count > 0; }

    private void EnsurePolling()
    {
        lock (_lock) _pollTimer ??= new System.Threading.Timer(PollState, null, 300, 300);
    }

    private void StopPolling()
    {
        System.Threading.Timer? t;
        lock (_lock) { t = _pollTimer; _pollTimer = null; }
        t?.Dispose();
    }

    private void PollState(object? _)
    {
        var path = AppConfig.Instance.StateFilePath;
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<StateEntry>>(json);
            if (entries is null) return;
            foreach (var entry in entries)
                StateUpdated?.Invoke(this, entry);
        }
        catch { /* transient I/O error — next poll will retry */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        lock (_lock)
        {
            foreach (var cts in _running.Values) cts.Cancel();
            _running.Clear();
        }
    }
}
