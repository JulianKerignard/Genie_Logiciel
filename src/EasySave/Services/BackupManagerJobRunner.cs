using EasySave.Shared;

namespace EasySave.Services;

/// <summary>
/// V3 <see cref="IJobRunner"/> that runs each parallel job through the v2
/// <see cref="BackupManager"/> pipeline. The orchestrator owns the
/// parallelism cap, per-job <see cref="JobExecutionContext"/>, and the
/// fan-out of progress; this runner is the glue that hands a single
/// execution off to <c>BackupManager.ExecuteJob</c> with the right
/// cancellation token.
/// </summary>
/// <remarks>
/// Progress fan-out to the orchestrator's <c>ProgressChanged</c> event is
/// handled via the existing <see cref="StateTracker.JobProgressChanged"/>
/// event chain — the bridges installed at app boot
/// (<c>StateTrackerEventBridge</c>) already publish on the bus, and the UI
/// listens to <c>BackupManagerAdapter.StateUpdated</c>. The
/// <paramref name="publishProgress"/> callback here is invoked only to
/// give the orchestrator's own subscribers a JobProgressDto snapshot at
/// the start and end of the job; mid-run snapshots flow through the bus.
/// </remarks>
public sealed class BackupManagerJobRunner : IJobRunner
{
    private readonly BackupManager _backupManager;

    public BackupManagerJobRunner(BackupManager backupManager)
    {
        ArgumentNullException.ThrowIfNull(backupManager);
        _backupManager = backupManager;
    }

    public Task RunAsync(JobExecutionContext context, Action<JobProgressDto> publishProgress)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(publishProgress);

        // Initial snapshot so subscribers see the job switch to Running
        // even before the first file boundary inside BackupManager.
        publishProgress(context.Progress with { State = JobStateEnum.Running });

        // ExecuteJob is synchronous; run it on the thread pool with the
        // per-job CTS so Stop()/Cancel() unwind it at the next file
        // boundary without disturbing sibling jobs.
        return Task.Run(
            () => _backupManager.ExecuteJob(context.JobName, ct: context.Cts.Token),
            context.Cts.Token);
    }
}
