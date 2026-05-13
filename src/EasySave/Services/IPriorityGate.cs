namespace EasySave.Services;

// Cross-job barrier enforcing the v3 CdC rule:
//
//   « Aucune sauvegarde d'un fichier non prioritaire ne peut se faire
//     tant qu'il y a des extensions prioritaires en attente sur au moins
//     un travail. »
//
// Each running job registers its count of priority-extension files up
// front, decrements the count after copying each one, and unregisters
// when it finishes. Non-priority files inside any running job block on
// WaitForNonPriorityWindow until the sum of priority counts across every
// job in the gate reaches zero — at which point every parked thread is
// released and subsequent non-priority files pass straight through.
//
// Thread-safe; designed for the v3 ParallelBackupOrchestrator's worker
// threads. A single PriorityGate instance is shared across every job in
// the engine so the cross-job barrier is global.
public interface IPriorityGate
{
    // Adds jobName to the live set with priorityFileCount files pending.
    // Idempotent — calling it twice for the same name overwrites the
    // count. Negative counts are rejected.
    void RegisterJob(string jobName, int priorityFileCount);

    // Decrements jobName's priority count by one. No-op when jobName is
    // not registered or its count is already zero. When the cross-job
    // total reaches zero the gate signals and every WaitForNonPriority
    // Window blocked thread wakes.
    void MarkPriorityFileDone(string jobName);

    // Blocks until the sum of priority counts across every registered
    // job is zero. Token-aware: a cancelled token throws
    // OperationCanceledException on the standard ManualResetEventSlim
    // contract — the slot is not consumed (there's no slot, it's a gate).
    // Returns immediately when there are no priority files anywhere.
    void WaitForNonPriorityWindow(CancellationToken ct);

    // Removes jobName from the live set. Should be called in the job's
    // finally — leftover priority counts (job cancelled or failed before
    // copying all priority files) are discarded so other jobs' non-
    // priority files are not held hostage forever.
    void UnregisterJob(string jobName);
}
