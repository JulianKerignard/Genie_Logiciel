using EasySave.Services;

namespace EasySave.Tests.V2;

public class BigFileGateTests
{
    // Small wait used to assert "task is still pending" without the test
    // hanging if the assertion is wrong. Long enough to absorb scheduler
    // jitter on CI; short enough to keep the suite fast.
    private static readonly TimeSpan PendingProbe = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void Constructor_NegativeThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BigFileGate(largeFileThresholdBytes: -1));
    }

    [Fact]
    public void LargeFileThresholdBytes_ExposesConstructorValue()
    {
        using var gate = new BigFileGate(largeFileThresholdBytes: 4096);
        Assert.Equal(4096, gate.LargeFileThresholdBytes);
    }

    [Fact]
    public async Task AcquireAsync_NegativeFileSize_Throws()
    {
        using var gate = new BigFileGate(largeFileThresholdBytes: 100);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => gate.AcquireAsync(fileSizeBytes: -1, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireAsync_SmallFile_ReturnsImmediately()
    {
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);

        using var handle = await gate.AcquireAsync(fileSizeBytes: 100, CancellationToken.None);

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task AcquireAsync_SmallFile_BypassesGateEvenWhileLargeFileHoldsIt()
    {
        // The whole point of the gate: small files must NOT be serialized
        // behind a large transfer. Hold the gate with a large file then
        // confirm a small acquire completes without waiting.
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);

        using var hugeHandle = await gate.AcquireAsync(
            fileSizeBytes: 10_000, CancellationToken.None);

        var smallAcquire = gate.AcquireAsync(
            fileSizeBytes: 100, CancellationToken.None);

        var winner = await Task.WhenAny(smallAcquire, Task.Delay(PendingProbe));
        Assert.Same(smallAcquire, winner);

        using var smallHandle = await smallAcquire;
        Assert.NotNull(smallHandle);
    }

    [Fact]
    public async Task AcquireAsync_FileExactlyAtThreshold_IsTreatedAsLarge()
    {
        // Spec: ">=" is the gating condition, so a file at exactly the
        // threshold takes the slot.
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);

        var handle1 = await gate.AcquireAsync(
            fileSizeBytes: 1024, CancellationToken.None);

        var acquire2 = gate.AcquireAsync(
            fileSizeBytes: 1024, CancellationToken.None);

        var winner = await Task.WhenAny(acquire2, Task.Delay(PendingProbe));
        Assert.NotSame(acquire2, winner);

        handle1.Dispose();
        using var handle2 = await acquire2;
        Assert.NotNull(handle2);
    }

    [Fact]
    public async Task AcquireAsync_LargeFile_BlocksUntilFirstSlotIsReleased()
    {
        using var gate = new BigFileGate(largeFileThresholdBytes: 100);

        var handle1 = await gate.AcquireAsync(
            fileSizeBytes: 1000, CancellationToken.None);

        var acquire2 = gate.AcquireAsync(
            fileSizeBytes: 1000, CancellationToken.None);

        // While handle1 is alive, acquire2 must stay pending.
        var winner = await Task.WhenAny(acquire2, Task.Delay(PendingProbe));
        Assert.NotSame(acquire2, winner);

        handle1.Dispose();

        // Releasing handle1 must unblock acquire2 promptly.
        using var handle2 = await acquire2;
        Assert.NotNull(handle2);
    }

    [Fact]
    public async Task AcquireAsync_LargeFile_AlreadyCancelledToken_ThrowsImmediately()
    {
        // Common shape: caller passes a token that has already been cancelled
        // (e.g. the orchestrator received Stop before the per-file acquire
        // ran). The acquire must throw without ever touching the slot.
        using var gate = new BigFileGate(largeFileThresholdBytes: 100);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gate.AcquireAsync(fileSizeBytes: 1000, cts.Token));

        // Slot was never taken — a fresh acquire completes immediately.
        using var handle = await gate.AcquireAsync(
            fileSizeBytes: 1000, CancellationToken.None);
        Assert.NotNull(handle);
    }

    [Fact]
    public async Task AcquireAsync_LargeFile_TokenCancelledWhileWaiting_ThrowsAndDoesNotConsumeSlot()
    {
        using var gate = new BigFileGate(largeFileThresholdBytes: 100);

        var handle1 = await gate.AcquireAsync(
            fileSizeBytes: 1000, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var acquire2 = gate.AcquireAsync(fileSizeBytes: 1000, cts.Token);

        // Confirm acquire2 is parked on the gate.
        var winner = await Task.WhenAny(acquire2, Task.Delay(PendingProbe));
        Assert.NotSame(acquire2, winner);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await acquire2);

        // Slot was not consumed by acquire2: handle1 still owns the only
        // slot, so a fresh waiter must keep waiting until handle1 releases.
        var acquire3 = gate.AcquireAsync(
            fileSizeBytes: 1000, CancellationToken.None);

        var winner3 = await Task.WhenAny(acquire3, Task.Delay(PendingProbe));
        Assert.NotSame(acquire3, winner3);

        handle1.Dispose();
        using var handle3 = await acquire3;
        Assert.NotNull(handle3);
    }

    [Fact]
    public async Task Dispose_LargeFileHandle_IsIdempotent()
    {
        // The internal SemaphoreSlim has maxCount = 1, so a double-Release
        // would throw SemaphoreFullException. The handle must short-circuit
        // a second Dispose so callers can safely combine `using` and an
        // explicit Dispose without crashing.
        using var gate = new BigFileGate(largeFileThresholdBytes: 100);

        var handle = await gate.AcquireAsync(
            fileSizeBytes: 1000, CancellationToken.None);

        handle.Dispose();
        handle.Dispose();

        // Slot should now be free — a fresh acquire completes without
        // blocking. If the second Dispose had double-released, this call
        // would have observed an over-counted semaphore (count = 2) and
        // a follow-up Release in another test would throw, but here the
        // immediate completion is what we verify.
        using var handle2 = await gate.AcquireAsync(
            fileSizeBytes: 1000, CancellationToken.None);
        Assert.NotNull(handle2);
    }

    [Fact]
    public async Task Dispose_SmallFileHandle_IsSafeToCallTwice()
    {
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);

        var handle = await gate.AcquireAsync(
            fileSizeBytes: 100, CancellationToken.None);

        handle.Dispose();
        handle.Dispose();
    }

    [Fact]
    public void SetThreshold_NegativeValue_Throws()
    {
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => gate.SetThreshold(largeFileThresholdBytes: -1));
    }

    [Fact]
    public async Task SetThreshold_ChangesGateDecisionOnNextAcquire()
    {
        // V3.1 hot-reload contract: the Settings UI calls SetThreshold after
        // the user saves. Files acquired BEFORE the change keep their decision;
        // the next acquisition sees the new threshold via Interlocked.Read.
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);

        // 500 bytes < 1024 → small-file fast path, returns immediately.
        using (var smallBefore = await gate.AcquireAsync(500, CancellationToken.None))
        {
            Assert.NotNull(smallBefore);
        }

        // Lower the threshold so the same 500 byte file is now "large".
        gate.SetThreshold(largeFileThresholdBytes: 100);

        Assert.Equal(100, gate.LargeFileThresholdBytes);

        // First acquire takes the slot; second must park until release.
        var holder = await gate.AcquireAsync(500, CancellationToken.None);
        var queued = gate.AcquireAsync(500, CancellationToken.None);

        var winner = await Task.WhenAny(queued, Task.Delay(PendingProbe));
        Assert.NotSame(queued, winner);

        holder.Dispose();
        using var next = await queued;
        Assert.NotNull(next);
    }

    [Fact]
    public async Task SetThreshold_RaisesThreshold_LargeFileBecomesSmallAndBypassesGate()
    {
        // Reverse direction: caller raised the threshold, so a previously-
        // gated size now bypasses the semaphore. Hold the slot with a 5000-
        // byte file, then a fresh 5000-byte acquire (under the new larger
        // threshold) must complete without parking.
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);

        var holder = await gate.AcquireAsync(5000, CancellationToken.None);
        gate.SetThreshold(largeFileThresholdBytes: 1_000_000);

        var fast = gate.AcquireAsync(5000, CancellationToken.None);
        var winner = await Task.WhenAny(fast, Task.Delay(PendingProbe));
        Assert.Same(fast, winner);

        using var fastHandle = await fast;
        Assert.NotNull(fastHandle);
        holder.Dispose();
    }

    [Fact]
    public async Task SetThreshold_ConcurrentWithAcquire_NoDeadlock()
    {
        // Stress: 50 worker tasks call AcquireAsync in a tight loop while a
        // separate writer flips the threshold back and forth. The volatile
        // (Interlocked) read in AcquireAsync must always see a consistent
        // value — never tear, never crash. Test passes if everything finishes
        // within the 3 s budget.
        using var gate = new BigFileGate(largeFileThresholdBytes: 1024);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writer = Task.Run(async () =>
        {
            long t = 64;
            while (!stop.IsCancellationRequested)
            {
                gate.SetThreshold(t);
                t = t == 64 ? 1_000_000 : 64;
                await Task.Yield();
            }
        });

        var workers = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var handle = await gate.AcquireAsync(500, stop.Token);
                    Assert.NotNull(handle);
                }
            }
            catch (OperationCanceledException) { /* expected at 3 s deadline */ }
        })).ToArray();

        await Task.WhenAll(workers.Append(writer));

        // If we reach here the gate didn't deadlock or crash.
        Assert.True(true);
    }
}
