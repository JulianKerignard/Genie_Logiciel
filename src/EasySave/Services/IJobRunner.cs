using EasySave.Shared;

namespace EasySave.Services;

// Strategy used by IParallelBackupOrchestrator to actually run each
// individual job. Decoupling lets the orchestrator focus on parallelism
// and isolation while the concrete runner takes care of the v3 backup
// pipeline (or, in tests, of arbitrary fake behaviour).
//
// The runner observes pause/stop through the supplied JobExecutionContext
// (Cts.Token for cancellation, PauseGate for pause/resume) and reports
// progress through publishProgress, which the orchestrator wires so each
// call both updates ctx.Progress and raises ProgressChanged on the worker
// thread.
//
// Hard requirement: every wait on PauseGate MUST use the token-aware
// overload PauseGate.Wait(ctx.Cts.Token). Stop() relies solely on
// CancellationToken to terminate the runner; a non-token-aware Wait would
// be unreachable from Stop() and the job would never end.
public interface IJobRunner
{
    Task RunAsync(JobExecutionContext context, Action<JobProgressDto> publishProgress);
}
