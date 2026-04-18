namespace EasySave;

// Singleton that tracks the real-time state of every backup job.
// Sits in the Model layer, ready to be observed by any View or ViewModel.
public sealed class StateTracker
{
    // Lazy, thread-safe singleton instance.
    private static readonly Lazy<StateTracker> _instance = new(() => new StateTracker());
    public static StateTracker Instance => _instance.Value;

    // In-memory store, keyed by job name (unique per workspace).
    private readonly Dictionary<string, StateEntry> _entries = new();

    private StateTracker() { }

    // Inserts or replaces the snapshot for a job, then persists the full state.
    public void Update(StateEntry entry)
    {
        throw new NotImplementedException();
    }
}
