using System.Text.Json;
using EasySave;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.UI.Services;

// Wraps the synchronous BackupManager to expose an async + pause/resume surface
// to the UI layer. Progress is tracked by polling state.json at 300 ms intervals.
// TODO: replace with real async BackupManager from dev2 when merged.
public sealed class BackupManagerAdapter : IBackupManagerAdapter
{
    private readonly BackupManager _manager;
    private readonly object _lock = new();
    private readonly Dictionary<string, CancellationTokenSource> _running = new();
    private readonly HashSet<string> _pausedByUs = new();
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

    public async Task RunJobAsync(string jobName, CancellationToken ct = default)
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
            // BackupManager.ExecuteJob is synchronous; run it on the thread pool.
            // Cancellation marks the job paused but cannot interrupt mid-file-copy.
            await Task.Run(() => _manager.ExecuteJob(jobName), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Fix: read IsCancellationRequested before releasing the CTS so the
            // race window between ExecuteJob returning and this finally block is
            // closed — PauseJob may have added the job to _pausedByUs during that
            // window; if the task completed normally we must evict the stale entry
            // to prevent an unwanted re-run on the next Resume call.
            bool cancelledByPause = cts.IsCancellationRequested;
            lock (_lock)
            {
                _running.Remove(jobName);
                if (!cancelledByPause) _pausedByUs.Remove(jobName);
                cts.Dispose();
            }
            if (!HasRunningJobs()) StopPolling();
        }
    }

    public void PauseJob(string jobName)
    {
        lock (_lock)
        {
            if (_running.TryGetValue(jobName, out var cts))
            {
                _pausedByUs.Add(jobName);
                cts.Cancel();
            }
        }
    }

    public void ResumeJob(string jobName)
    {
        bool wasPaused;
        lock (_lock) wasPaused = _pausedByUs.Remove(jobName);
        if (wasPaused) _ = RunJobAsync(jobName);
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
