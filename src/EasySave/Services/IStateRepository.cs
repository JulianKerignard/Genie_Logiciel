namespace EasySave.Services;

// Abstraction over the real-time job state store (state.json).
// V3 supports multiple concurrent jobs — implementations must be thread-safe.
// The backing format is a dictionary keyed by job name so callers never need
// to manage de-duplication by hand.
public interface IStateRepository
{
    // Inserts or updates the persisted state for a job. Thread-safe.
    void UpdateJob(string name, JobState state);

    // Returns the current snapshot for the given job, or null if unknown.
    StateEntry? GetJob(string name);

    // Returns a consistent snapshot of all currently tracked jobs.
    // Callers must not mutate the returned entries.
    IReadOnlyList<StateEntry> GetAll();

    // Removes the entry for the given job and rewrites the store. No-op if absent.
    void RemoveJob(string name);
}
