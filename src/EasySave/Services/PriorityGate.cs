namespace EasySave.Services;

// Concrete IPriorityGate using a ManualResetEventSlim as the cross-job
// barrier and a single lock protecting the per-job count map. The
// signaled state of the gate represents "no priority work pending
// anywhere"; non-priority files Wait the gate, priority files don't.
//
// All public methods are thread-safe. The lock scope is short and never
// straddles the underlying Wait — waiters never hold the lock while
// blocked.
public sealed class PriorityGate : IPriorityGate, IDisposable
{
    private readonly object _lock = new();
    // Job names canonicalised by JobRepository are case-stable, but the
    // rest of the system compares them with OrdinalIgnoreCase (see
    // BackupManager.ExecuteJob's job lookup); aligning the gate's lookup
    // removes a future footgun if a caller ever supplies a different-
    // case variant of the same job name.
    private readonly Dictionary<string, int> _remaining = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManualResetEventSlim _cleared = new(initialState: true);

    public void RegisterJob(string jobName, int priorityFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentOutOfRangeException.ThrowIfNegative(priorityFileCount);

        lock (_lock)
        {
            _remaining[jobName] = priorityFileCount;
            UpdateGateUnsafe();
        }
    }

    public void MarkPriorityFileDone(string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        lock (_lock)
        {
            if (!_remaining.TryGetValue(jobName, out var n) || n <= 0) return;
            _remaining[jobName] = n - 1;
            UpdateGateUnsafe();
        }
    }

    public void WaitForNonPriorityWindow(CancellationToken ct)
    {
        // Snapshot is taken inside the gate's own internal lock; no
        // explicit user-side lock needed here. Token-aware overload
        // surfaces OperationCanceledException through the standard
        // ManualResetEventSlim contract.
        _cleared.Wait(ct);
    }

    public void UnregisterJob(string jobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        lock (_lock)
        {
            if (!_remaining.Remove(jobName)) return;
            UpdateGateUnsafe();
        }
    }

    private void UpdateGateUnsafe()
    {
        var total = 0;
        foreach (var n in _remaining.Values)
        {
            total += n;
            if (total > 0) break;
        }
        if (total == 0) _cleared.Set();
        else _cleared.Reset();
    }

    public void Dispose() => _cleared.Dispose();
}
