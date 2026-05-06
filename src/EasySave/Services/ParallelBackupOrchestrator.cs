using System.Collections.Concurrent;
using EasyLog;
using EasySave.Shared;

namespace EasySave.Services;

// Concrete IParallelBackupOrchestrator. Runs every submitted job in
// parallel up to MaxParallelJobs, isolating per-job state in a dedicated
// JobExecutionContext so Pause / Resume / Stop on one job never disturb
// its siblings. The actual "what to do for each file in a job" is
// delegated to an injected IJobRunner — the orchestrator only owns
// concurrency, lifecycle and progress fan-out.
//
// Thread-safety:
//  - _running is a ConcurrentDictionary so Pause/Resume/Stop and the
//    runner's add/remove operations cannot corrupt the lookup.
//  - _slots is a SemaphoreSlim, internally thread-safe.
//  - ProgressChanged is invoked through a local snapshot of the delegate
//    field so a concurrent unsubscribe never throws NullReferenceException.
public sealed class ParallelBackupOrchestrator : IParallelBackupOrchestrator
{
    private readonly IJobRunner _runner;
    private readonly Func<string, IDailyLogger> _loggerFactory;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<string, JobExecutionContext> _running = new();

    public event Action<JobProgressDto> ProgressChanged = static _ => { };

    public ParallelBackupOrchestrator(
        IJobRunner runner,
        Func<string, IDailyLogger> loggerFactory,
        int maxParallelJobs)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelJobs, 1);

        _runner = runner;
        _loggerFactory = loggerFactory;
        _slots = new SemaphoreSlim(maxParallelJobs, maxParallelJobs);
    }

    public async Task<IReadOnlyList<JobResult>> RunAsync(
        IEnumerable<string> jobNames,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobNames);

        var names = jobNames.ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException(
                "Duplicate job names are not supported: Pause/Resume/Stop target by name.",
                nameof(jobNames));
        }

        var tasks = names.Select(n => RunSingleAsync(n, ct)).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<JobResult> RunSingleAsync(string jobName, CancellationToken batchCt)
    {
        var submittedAt = DateTimeOffset.UtcNow;

        // The slot wait is its own try/catch so a batch cancellation that
        // arrives before the job ever starts surfaces cleanly as a
        // Cancelled JobResult instead of throwing out of Task.WhenAll. The
        // ObjectDisposedException branch covers the Dispose-while-queued
        // race (see Dispose contract below); without it the queued task
        // would fault.
        try
        {
            await _slots.WaitAsync(batchCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new JobResult(
                jobName, JobOutcome.Cancelled, submittedAt,
                DateTimeOffset.UtcNow, Message: "batch cancelled before slot");
        }
        catch (ObjectDisposedException)
        {
            return new JobResult(
                jobName, JobOutcome.Cancelled, submittedAt,
                DateTimeOffset.UtcNow, Message: "orchestrator disposed before slot");
        }

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            return await ExecuteSingleAsync(jobName, startedAt, batchCt).ConfigureAwait(false);
        }
        finally
        {
            // Release can race with Dispose; the finally still has to run
            // to honour the per-job result contract, so swallow the ODE
            // rather than fault the task.
            try { _slots.Release(); }
            catch (ObjectDisposedException) { /* dispose race */ }
        }
    }

    private async Task<JobResult> ExecuteSingleAsync(
        string jobName,
        DateTimeOffset startedAt,
        CancellationToken batchCt)
    {
        using var ctx = new JobExecutionContext(jobName, _loggerFactory(jobName));

        // Bridge the batch token to the per-job CTS so a single batch.Cancel()
        // tears every running job down through their own JobExecutionContext.
        using var registration = batchCt.Register(static state =>
            ((CancellationTokenSource)state!).Cancel(), ctx.Cts);

        if (!_running.TryAdd(jobName, ctx))
        {
            return new JobResult(
                jobName, JobOutcome.Failed, startedAt,
                DateTimeOffset.UtcNow,
                Message: $"job '{jobName}' is already running in this orchestrator");
        }

        try
        {
            ctx.Progress = ctx.Progress with { State = JobStateEnum.Running };

            await _runner.RunAsync(ctx, snapshot =>
            {
                ctx.Progress = snapshot;
                // Snapshot the delegate before invoking so a concurrent -=
                // can't reorder against this read. The event is seeded with
                // a no-op so the field is never null even with zero
                // subscribers.
                var handler = ProgressChanged;
                handler.Invoke(snapshot);
            }).ConfigureAwait(false);

            return new JobResult(
                jobName, JobOutcome.Succeeded, startedAt, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException oce)
        {
            string reason;
            if (batchCt.IsCancellationRequested)
                reason = "batch cancelled";
            else if (ctx.Cts.IsCancellationRequested)
                reason = "stopped via Stop()";
            else
                reason = oce.Message;

            return new JobResult(
                jobName, JobOutcome.Cancelled, startedAt,
                DateTimeOffset.UtcNow, Message: reason);
        }
        catch (Exception ex)
        {
            return new JobResult(
                jobName, JobOutcome.Failed, startedAt,
                DateTimeOffset.UtcNow, Message: ex.Message);
        }
        finally
        {
            _running.TryRemove(jobName, out _);
        }
    }

    public void Pause(string jobName)
    {
        if (_running.TryGetValue(jobName, out var ctx))
            ctx.PauseGate.Reset();
    }

    public void Resume(string jobName)
    {
        if (_running.TryGetValue(jobName, out var ctx))
            ctx.PauseGate.Set();
    }

    public void Stop(string jobName)
    {
        // Cancel only — do not pulse PauseGate. Pulsing the gate races with
        // the cancellation: ManualResetEventSlim.Wait returns success when
        // the event is set even if the token is cancelled in the same
        // window, so a runner could exit Wait normally and produce a
        // Succeeded result. Runners that block on the gate must use the
        // token-aware overload (PauseGate.Wait(ctx.Cts.Token)) to surface
        // OCE deterministically — that's the standard .NET cancellation
        // pattern and a hard requirement for IJobRunner implementations.
        if (_running.TryGetValue(jobName, out var ctx))
            ctx.Cts.Cancel();
    }

    // Dispose initiates shutdown by cancelling every in-flight job's CTS
    // (so token-aware runners exit promptly with Cancelled) and disposes the
    // parallelism semaphore. Callers should still await every outstanding
    // RunAsync(...) call before Dispose to collect the per-job results —
    // this matches the contract of HttpClient and IBackupManagerAdapter.
    // RunSingleAsync is defensive against the Dispose-during-flight race
    // (ObjectDisposedException on slot wait or release surface as Cancelled)
    // so an out-of-order Dispose never faults the running batch task.
    public void Dispose()
    {
        foreach (var ctx in _running.Values)
        {
            try { ctx.Cts.Cancel(); }
            catch (ObjectDisposedException) { /* race with self-disposal */ }
        }
        _slots.Dispose();
    }
}
