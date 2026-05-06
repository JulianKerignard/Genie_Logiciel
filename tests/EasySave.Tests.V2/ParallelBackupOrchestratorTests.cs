using EasyLog;
using EasySave.Services;
using EasySave.Shared;

namespace EasySave.Tests.V2;

public class ParallelBackupOrchestratorTests
{
    // Lets the test author plug arbitrary per-job behaviour into the
    // orchestrator without standing up the real backup pipeline.
    private sealed class FakeJobRunner : IJobRunner
    {
        private readonly Func<JobExecutionContext, Action<JobProgressDto>, Task> _impl;

        public FakeJobRunner(Func<JobExecutionContext, Action<JobProgressDto>, Task> impl)
            => _impl = impl;

        public Task RunAsync(JobExecutionContext context, Action<JobProgressDto> publishProgress)
            => _impl(context, publishProgress);
    }

    private sealed class NoOpLogger : IDailyLogger
    {
        public void Append(LogEntry entry) { }
    }

    private static ParallelBackupOrchestrator Make(
        Func<JobExecutionContext, Action<JobProgressDto>, Task> runImpl,
        int maxParallelJobs = 4)
    {
        return new ParallelBackupOrchestrator(
            new FakeJobRunner(runImpl),
            _ => new NoOpLogger(),
            maxParallelJobs);
    }

    // ---- argument validation ----

    [Fact]
    public void Constructor_NullRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ParallelBackupOrchestrator(null!, _ => new NoOpLogger(), 1));
    }

    [Fact]
    public void Constructor_MaxParallelJobsZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParallelBackupOrchestrator(
                new FakeJobRunner((_, _) => Task.CompletedTask),
                _ => new NoOpLogger(),
                maxParallelJobs: 0));
    }

    [Fact]
    public async Task RunAsync_DuplicateJobNames_ThrowsArgumentException()
    {
        using var orch = Make((_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            orch.RunAsync(new[] { "A", "B", "A" }, CancellationToken.None));
    }

    // ---- happy paths ----

    [Fact]
    public async Task RunAsync_EmptyList_ReturnsEmptyResults()
    {
        using var orch = Make((_, _) => Task.CompletedTask);

        var results = await orch.RunAsync(Array.Empty<string>(), CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task RunAsync_SingleJob_CompletesSucceeded()
    {
        using var orch = Make((_, _) => Task.CompletedTask);

        var results = await orch.RunAsync(new[] { "job-A" }, CancellationToken.None);

        var only = Assert.Single(results);
        Assert.Equal("job-A", only.JobName);
        Assert.Equal(JobOutcome.Succeeded, only.Outcome);
        Assert.Null(only.Message);
    }

    [Fact]
    public async Task RunAsync_PreservesInputOrderInResults()
    {
        // Each runner sleeps a different amount so completion order does
        // not match submission order; the orchestrator must still return
        // the results in the original sequence.
        using var orch = Make(async (ctx, _) =>
        {
            int delay = ctx.JobName switch
            {
                "c" => 30,
                "a" => 5,
                "b" => 20,
                "d" => 10,
                _ => 0
            };
            await Task.Delay(delay);
        });

        var input = new[] { "c", "a", "b", "d" };
        var results = await orch.RunAsync(input, CancellationToken.None);

        Assert.Equal(input, results.Select(r => r.JobName).ToArray());
    }

    [Fact]
    public async Task RunAsync_RunnerThrows_OutcomeIsFailedWithMessage()
    {
        using var orch = Make((_, _) => throw new InvalidOperationException("boom"));

        var results = await orch.RunAsync(new[] { "A" }, CancellationToken.None);

        Assert.Equal(JobOutcome.Failed, results[0].Outcome);
        Assert.Equal("boom", results[0].Message);
    }

    // ---- cancellation ----

    [Fact]
    public async Task RunAsync_BatchTokenCancelledMidFlight_AllInFlightJobsAreCancelled()
    {
        var bothStarted = new TaskCompletionSource();
        var startedCount = 0;

        using var orch = Make(async (ctx, _) =>
        {
            if (Interlocked.Increment(ref startedCount) == 2)
                bothStarted.SetResult();
            await Task.Delay(Timeout.Infinite, ctx.Cts.Token);
        }, maxParallelJobs: 2);

        using var cts = new CancellationTokenSource();
        var task = orch.RunAsync(new[] { "A", "B" }, cts.Token);

        await bothStarted.Task;
        cts.Cancel();

        var results = await task;

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(JobOutcome.Cancelled, r.Outcome));
        Assert.All(results, r => Assert.Equal("batch cancelled", r.Message));
    }

    [Fact]
    public async Task RunAsync_BatchTokenCancelledBeforeSlot_QueuedJobsReportCancelledBeforeSlot()
    {
        // cap = 1 so only one job runs at a time. The first job blocks
        // forever; we cancel the batch while jobs B and C are still
        // queued for the slot.
        var firstStarted = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();

        using var orch = Make(async (ctx, _) =>
        {
            if (ctx.JobName == "A") firstStarted.SetResult();
            await releaseFirst.Task.WaitAsync(ctx.Cts.Token);
        }, maxParallelJobs: 1);

        using var cts = new CancellationTokenSource();
        var task = orch.RunAsync(new[] { "A", "B", "C" }, cts.Token);

        await firstStarted.Task;
        cts.Cancel();
        releaseFirst.TrySetResult();

        var results = await task;

        Assert.Equal(JobOutcome.Cancelled, results[0].Outcome);
        Assert.Equal("batch cancelled before slot", results[1].Message);
        Assert.Equal("batch cancelled before slot", results[2].Message);
    }

    // ---- parallelism cap ----

    [Fact]
    public async Task RunAsync_RespectsMaxParallelJobsCap()
    {
        int inFlight = 0;
        int observedMax = 0;
        var releaseAll = new TaskCompletionSource();

        using var orch = Make(async (_, _) =>
        {
            int now = Interlocked.Increment(ref inFlight);
            // Race-free max tracking via CAS loop.
            int prev;
            do
            {
                prev = observedMax;
                if (now <= prev) break;
            } while (Interlocked.CompareExchange(ref observedMax, now, prev) != prev);

            await releaseAll.Task;
            Interlocked.Decrement(ref inFlight);
        }, maxParallelJobs: 2);

        var task = orch.RunAsync(new[] { "A", "B", "C", "D" }, CancellationToken.None);

        // Give the scheduler a beat for jobs A and B to enter the runner.
        // With cap = 2, observedMax must never exceed 2.
        await Task.Delay(150);
        Assert.Equal(2, observedMax);

        releaseAll.SetResult();
        await task;

        Assert.Equal(2, observedMax);
    }

    // ---- pause / resume / stop ----

    [Fact]
    public async Task Pause_ResetsTargetJobsPauseGate_OtherJobsUnaffected()
    {
        var aStarted = new TaskCompletionSource();
        var bStarted = new TaskCompletionSource();
        var inspect = new TaskCompletionSource();
        var observedAGate = false;
        var observedBGate = false;

        using var orch = Make(async (ctx, _) =>
        {
            if (ctx.JobName == "A") aStarted.SetResult();
            else bStarted.SetResult();
            await inspect.Task;
            if (ctx.JobName == "A") observedAGate = ctx.PauseGate.IsSet;
            else observedBGate = ctx.PauseGate.IsSet;
        }, maxParallelJobs: 2);

        var task = orch.RunAsync(new[] { "A", "B" }, CancellationToken.None);

        await Task.WhenAll(aStarted.Task, bStarted.Task);

        orch.Pause("A");
        inspect.SetResult();

        await task;

        Assert.False(observedAGate);    // A was paused
        Assert.True(observedBGate);     // B untouched
    }

    [Fact]
    public async Task Resume_SetsTargetJobsPauseGateAfterPause()
    {
        var started = new TaskCompletionSource();
        var inspect = new TaskCompletionSource();
        var gateAfterResume = false;

        using var orch = Make(async (ctx, _) =>
        {
            started.SetResult();
            await inspect.Task;
            gateAfterResume = ctx.PauseGate.IsSet;
        }, maxParallelJobs: 1);

        var task = orch.RunAsync(new[] { "A" }, CancellationToken.None);

        await started.Task;
        orch.Pause("A");
        orch.Resume("A");
        inspect.SetResult();

        await task;

        Assert.True(gateAfterResume);
    }

    [Fact]
    public async Task Stop_CancelsTargetJob_OtherJobsCompleteSucceeded()
    {
        var aStarted = new TaskCompletionSource();
        var bDone = new TaskCompletionSource();

        using var orch = Make(async (ctx, _) =>
        {
            if (ctx.JobName == "A")
            {
                aStarted.SetResult();
                await Task.Delay(Timeout.Infinite, ctx.Cts.Token);
            }
            else
            {
                bDone.SetResult();
            }
        }, maxParallelJobs: 2);

        var task = orch.RunAsync(new[] { "A", "B" }, CancellationToken.None);

        await Task.WhenAll(aStarted.Task, bDone.Task);
        orch.Stop("A");

        var results = await task;

        Assert.Equal(JobOutcome.Cancelled, results[0].Outcome);
        Assert.Equal("stopped via Stop()", results[0].Message);
        Assert.Equal(JobOutcome.Succeeded, results[1].Outcome);
    }

    [Fact]
    public async Task Stop_OnJobBlockedOnPauseGate_ProducesCancelled()
    {
        // The runner closes the gate itself before parking so there is no
        // race over whether Pause("A") arrived before the runner reached
        // Wait. Task.Run pushes the blocking Wait onto a thread-pool thread;
        // without it the synchronous Wait would block the orchestrator's
        // continuation on the test thread and deadlock.
        var started = new TaskCompletionSource();

        using var orch = Make((ctx, _) => Task.Run(() =>
        {
            ctx.PauseGate.Reset();
            started.SetResult();
            ctx.PauseGate.Wait(ctx.Cts.Token);
        }), maxParallelJobs: 1);

        var task = orch.RunAsync(new[] { "A" }, CancellationToken.None);

        await started.Task;
        orch.Stop("A");

        var results = await task;
        Assert.Equal(JobOutcome.Cancelled, results[0].Outcome);
        Assert.Equal("stopped via Stop()", results[0].Message);
    }

    [Theory]
    [InlineData("Pause")]
    [InlineData("Resume")]
    [InlineData("Stop")]
    public void PauseResumeStop_OnUnknownJobName_AreNoOps(string verb)
    {
        using var orch = Make((_, _) => Task.CompletedTask);

        // Should not throw despite no job ever being registered.
        switch (verb)
        {
            case "Pause": orch.Pause("ghost"); break;
            case "Resume": orch.Resume("ghost"); break;
            case "Stop": orch.Stop("ghost"); break;
        }
    }

    // ---- progress fan-out ----

    [Fact]
    public async Task ProgressChanged_FiresWhenRunnerPublishesAndReachesSubscribers()
    {
        var captured = new List<JobProgressDto>();
        var snapshot = new JobProgressDto(
            JobName: "A",
            State: JobStateEnum.Running,
            CurrentFile: "file-1.bin",
            FilesLeft: 4,
            TotalFiles: 5,
            BytesLeft: 100,
            BytesTotal: 200);

        using var orch = Make((_, publish) =>
        {
            publish(snapshot);
            return Task.CompletedTask;
        });

        orch.ProgressChanged += p =>
        {
            lock (captured) captured.Add(p);
        };

        await orch.RunAsync(new[] { "A" }, CancellationToken.None);

        Assert.Single(captured);
        Assert.Equal(snapshot, captured[0]);
    }
}
