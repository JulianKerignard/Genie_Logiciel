using EasySave.Services;

namespace EasySave.Tests.V2;

public class PriorityGateTests
{
    private static readonly TimeSpan PendingProbe = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void Constructor_NewGate_AllowsNonPriorityImmediately()
    {
        using var gate = new PriorityGate();
        // No job registered → gate is signaled → Wait returns immediately.
        gate.WaitForNonPriorityWindow(CancellationToken.None);
    }

    [Fact]
    public void RegisterJob_WithZeroPriorityFiles_DoesNotBlockWaiters()
    {
        using var gate = new PriorityGate();
        gate.RegisterJob("A", priorityFileCount: 0);
        gate.WaitForNonPriorityWindow(CancellationToken.None);
    }

    [Fact]
    public async Task RegisterJob_WithPriorityFiles_BlocksNonPriorityUntilAllDone()
    {
        // Single job with 3 priority files: a non-priority waiter must
        // stay parked until all 3 are marked done.
        using var gate = new PriorityGate();
        gate.RegisterJob("A", priorityFileCount: 3);

        var waitTask = Task.Run(() => gate.WaitForNonPriorityWindow(CancellationToken.None));

        // Still pending after 100 ms — 3 priorities outstanding.
        var winner = await Task.WhenAny(waitTask, Task.Delay(PendingProbe));
        Assert.NotSame(waitTask, winner);

        gate.MarkPriorityFileDone("A");
        gate.MarkPriorityFileDone("A");
        // 1 priority left — still blocked.
        var winner2 = await Task.WhenAny(waitTask, Task.Delay(PendingProbe));
        Assert.NotSame(waitTask, winner2);

        gate.MarkPriorityFileDone("A");
        // Total reached zero — waiter must unblock promptly.
        await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CrossJob_ANonPriorityBlocksWhileBHasPrioritiesPending()
    {
        // Two jobs. A has zero priorities, B has one. A's non-priority
        // file must wait for B to finish even though A itself has no
        // priorities — that's the CdC rule.
        using var gate = new PriorityGate();
        gate.RegisterJob("A", priorityFileCount: 0);
        gate.RegisterJob("B", priorityFileCount: 1);

        var waitTask = Task.Run(() => gate.WaitForNonPriorityWindow(CancellationToken.None));

        var winner = await Task.WhenAny(waitTask, Task.Delay(PendingProbe));
        Assert.NotSame(waitTask, winner);

        gate.MarkPriorityFileDone("B");

        await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UnregisterJob_DiscardsLeftoverPrioritiesAndUnblocksOthers()
    {
        // Job A registered with 5 priorities, no MarkDone calls (think:
        // the job was cancelled mid-run). Unregister must drop the live
        // count so any other job's non-priority files stop waiting.
        using var gate = new PriorityGate();
        gate.RegisterJob("A", priorityFileCount: 5);
        gate.RegisterJob("B", priorityFileCount: 0);

        var waitTask = Task.Run(() => gate.WaitForNonPriorityWindow(CancellationToken.None));

        var winner = await Task.WhenAny(waitTask, Task.Delay(PendingProbe));
        Assert.NotSame(waitTask, winner);

        gate.UnregisterJob("A");

        await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WaitForNonPriorityWindow_CancelledToken_ThrowsWithoutCorruptingGate()
    {
        // A cancelled waiter does not consume any slot (this is a gate,
        // not a semaphore) and does not change the priority count, so a
        // subsequent waiter sees the same outstanding priorities.
        using var gate = new PriorityGate();
        gate.RegisterJob("A", priorityFileCount: 1);

        using var cts = new CancellationTokenSource();
        var firstWait = Task.Run(() =>
            Assert.ThrowsAny<OperationCanceledException>(
                () => gate.WaitForNonPriorityWindow(cts.Token)));

        var winner = await Task.WhenAny(firstWait, Task.Delay(PendingProbe));
        Assert.NotSame(firstWait, winner);
        cts.Cancel();
        await firstWait;

        // Sanity: second waiter still blocks (priority count untouched).
        var secondWait = Task.Run(() => gate.WaitForNonPriorityWindow(CancellationToken.None));
        var winner2 = await Task.WhenAny(secondWait, Task.Delay(PendingProbe));
        Assert.NotSame(secondWait, winner2);

        gate.MarkPriorityFileDone("A");
        await secondWait.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MarkPriorityFileDone_OnUnknownJobName_IsNoOp()
    {
        using var gate = new PriorityGate();
        // Should not throw even though nothing is registered.
        gate.MarkPriorityFileDone("ghost");
        gate.WaitForNonPriorityWindow(CancellationToken.None);
    }

    [Fact]
    public void MarkPriorityFileDone_OnAlreadyZeroJob_IsNoOpDoesNotGoNegative()
    {
        // Register with 1 priority, mark twice. Second mark must not
        // drive the count negative (would break the "total > 0" check).
        using var gate = new PriorityGate();
        gate.RegisterJob("A", priorityFileCount: 1);
        gate.MarkPriorityFileDone("A");
        gate.MarkPriorityFileDone("A");

        // Register a new job with 1 priority; the gate must block again.
        gate.RegisterJob("B", priorityFileCount: 1);
        Assert.Throws<OperationCanceledException>(() =>
            gate.WaitForNonPriorityWindow(new CancellationToken(canceled: true)));
    }

    [Fact]
    public void RegisterJob_NegativeCount_Throws()
    {
        using var gate = new PriorityGate();
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.RegisterJob("A", -1));
    }

    [Fact]
    public void RegisterJob_NullOrWhitespaceName_Throws()
    {
        using var gate = new PriorityGate();
        Assert.Throws<ArgumentException>(() => gate.RegisterJob("", 1));
        Assert.Throws<ArgumentException>(() => gate.RegisterJob("   ", 1));
    }
}
