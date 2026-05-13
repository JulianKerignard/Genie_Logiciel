using EasySave.Shared;

namespace EasySave.Services;

// Coordinates the parallel execution of multiple backup jobs in v3,
// replacing the sequential v2 BackupManager.ExecuteJob loop. The number of
// concurrent jobs is bounded by AppSettings.MaxParallelJobs, configured at
// startup; jobs submitted beyond that cap queue and start as slots free up.
//
// All operations are thread-safe so Pause/Resume/Stop can be called from any
// thread (typically the UI dispatcher or the remote console handler) while
// RunAsync is in flight. Each running job is driven through its own
// JobExecutionContext.
//
// Implementations own per-job CancellationTokenSources and a semaphore for
// the parallelism cap, hence the IDisposable surface for app-shutdown
// cleanup (matching the precedent set by IBackupManagerAdapter).
//
// Per-job and "All" control methods (Pause / Resume / Stop / PauseAll /
// ResumeAll / StopAll) come from the inherited IJobController interface
// so the watcher and the remote-command handler can depend on the
// narrower control surface instead of the full orchestrator.
public interface IParallelBackupOrchestrator : IJobController, IDisposable
{
    // Runs every job in jobNames concurrently, bounded by MaxParallelJobs,
    // and completes when all of them have finished, failed or been
    // cancelled. ct cancels the whole batch — every running job receives
    // the cancellation through its own JobExecutionContext and surfaces as
    // JobOutcome.Cancelled in the result list.
    //
    // The returned list contains exactly one JobResult per submitted name,
    // in the same order as jobNames. Throws ArgumentException when jobNames
    // contains duplicates, since Pause/Resume/Stop target by name and would
    // otherwise be ambiguous.
    Task<IReadOnlyList<JobResult>> RunAsync(
        IEnumerable<string> jobNames,
        CancellationToken ct);

    // Raised every time a running job's progress snapshot is updated.
    // Subscribers must be thread-safe: handlers run on the worker thread
    // that produced the snapshot, not on a UI dispatcher.
    event Action<JobProgressDto> ProgressChanged;
}
