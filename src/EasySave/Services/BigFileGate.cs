namespace EasySave.Services;

// Concrete IBigFileGate using a single-slot SemaphoreSlim to serialize the
// transfer of files at or above the threshold. Files below the threshold
// bypass the semaphore entirely and acquire a no-op handle so the
// using-pattern at the call site stays uniform regardless of file size.
//
// Thread-safe: SemaphoreSlim handles concurrent waiters, the no-op handle
// is stateless, and the release handle uses Interlocked.Exchange to make
// Dispose idempotent in case a caller wraps the handle in `using` and also
// disposes it manually.
public sealed class BigFileGate : IBigFileGate, IDisposable
{
    private static readonly IDisposable _smallFileHandle = new NoOpHandle();

    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    // Mutated by SetThreshold from the UI thread, read by every AcquireAsync
    // caller from the worker pool. Interlocked.Read/Exchange on long is the
    // canonical lock-free pattern (the `volatile` keyword cannot be applied
    // to long since long is not guaranteed atomic on 32-bit platforms).
    private long _thresholdBytes;

    public long LargeFileThresholdBytes => Interlocked.Read(ref _thresholdBytes);

    public BigFileGate(long largeFileThresholdBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(largeFileThresholdBytes);
        _thresholdBytes = largeFileThresholdBytes;
    }

    public async Task<IDisposable> AcquireAsync(long fileSizeBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileSizeBytes);

        // Snapshot once: a concurrent SetThreshold should not change the
        // decision mid-method. The next AcquireAsync call will see the new
        // value via Interlocked.Read in the LargeFileThresholdBytes getter.
        long threshold = Interlocked.Read(ref _thresholdBytes);
        if (fileSizeBytes < threshold)
            return _smallFileHandle;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return new ReleaseHandle(_gate);
    }

    public void SetThreshold(long largeFileThresholdBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(largeFileThresholdBytes);
        Interlocked.Exchange(ref _thresholdBytes, largeFileThresholdBytes);
    }

    // The host is expected to stop every running job (and let their gate
    // handles fall out of scope) before disposing the gate itself. If a
    // waiter is still parked in WaitAsync when Dispose runs, it surfaces as
    // ObjectDisposedException on that waiter rather than the OCE that
    // cancellation would deliver — caller's responsibility to avoid the
    // race during shutdown.
    public void Dispose() => _gate.Dispose();

    private sealed class NoOpHandle : IDisposable
    {
        public void Dispose() { }
    }

    // Holds the semaphore reference behind a nullable field so Dispose can
    // atomically swap it to null and skip a second Release. Without this
    // guard, a double-Dispose would call Release on a full semaphore and
    // throw SemaphoreFullException.
    private sealed class ReleaseHandle : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public ReleaseHandle(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            var sem = Interlocked.Exchange(ref _semaphore, null);
            if (sem is null) return;
            try
            {
                sem.Release();
            }
            catch (ObjectDisposedException)
            {
                // The gate was disposed while this copy was in flight (host
                // shutdown race). Releasing a disposed semaphore would
                // otherwise fault the worker task and report the job as
                // Failed even though the copy succeeded. Swallow — the gate
                // is gone for good, no other waiter is parked on it.
            }
        }
    }
}
