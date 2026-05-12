namespace EasySave.Services;

// UI-/watcher-facing control surface over every job currently running in
// the v3 parallel orchestrator. Operates by job name for fine-grained
// control (a remote console clicking Pause on a single row), and exposes
// "All" variants for blanket actions — typically driven by the business-
// software watcher when a watched process appears or disappears.
//
// IParallelBackupOrchestrator inherits this interface so the orchestrator
// stays the single source of truth for the live job map; consumers that
// only need control (the watcher, the remote-command handler) can depend
// on the narrower IJobController surface instead of the full orchestrator.
//
// All methods are thread-safe and no-op when the targeted job is not in
// the matching state — calling Pause on a job that already paused, Resume
// on a running job, or any operation on an unknown job name does not
// throw.
public interface IJobController
{
    // Pauses the named running job at the next file boundary. The pause
    // takes effect after the file currently being copied finishes, so the
    // target file is never left in a partial state.
    void Pause(string jobName);

    // Resumes a previously paused job by signaling its PauseGate so the
    // worker thread parked between two files wakes up and continues.
    void Resume(string jobName);

    // Stops the named job through its CancellationToken — works on both
    // Running and Paused jobs. The job's RunAsync returns with
    // JobOutcome.Cancelled.
    void Stop(string jobName);

    // Pauses every job currently registered in the orchestrator. Idempotent
    // (jobs already paused stay paused). Used by the business-software
    // watcher when a watched process is detected.
    void PauseAll();

    // Resumes every job currently registered in the orchestrator. Mirror
    // of PauseAll — used by the watcher when the watched process exits.
    void ResumeAll();

    // Cancels every job currently registered in the orchestrator. Each
    // job returns Cancelled from RunAsync.
    void StopAll();
}
