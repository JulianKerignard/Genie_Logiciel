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
public interface IParallelBackupOrchestrator
{
    // Runs every job in jobNames concurrently, bounded by MaxParallelJobs,
    // and completes when all of them have finished, failed or been
    // cancelled. ct cancels the whole batch — every running job receives
    // the cancellation through its own JobExecutionContext and surfaces as
    // JobOutcome.Cancelled in the result list.
    //
    // The returned list contains exactly one JobResult per submitted name,
    // in the same order as jobNames.
    Task<IReadOnlyList<JobResult>> RunAsync(
        IEnumerable<string> jobNames,
        CancellationToken ct);

    // Pauses the named running job at the next file boundary. No-op when
    // the job is not currently running or is already paused.
    void Pause(string jobName);

    // Resumes a previously paused job. No-op when the job is not paused.
    void Resume(string jobName);

    // Stops the named job. The job exits with JobOutcome.Cancelled. No-op
    // when the job is not currently running.
    void Stop(string jobName);

    // Raised every time a running job's progress snapshot is updated.
    // Subscribers must be thread-safe: handlers run on the worker thread
    // that produced the snapshot, not on a UI dispatcher.
    event Action<JobProgressDto> ProgressChanged;
}
