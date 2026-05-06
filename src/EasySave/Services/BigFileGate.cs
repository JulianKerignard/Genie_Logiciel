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

    public long LargeFileThresholdBytes { get; }

    public BigFileGate(long largeFileThresholdBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(largeFileThresholdBytes);
        LargeFileThresholdBytes = largeFileThresholdBytes;
    }

    public async Task<IDisposable> AcquireAsync(long fileSizeBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileSizeBytes);

        if (fileSizeBytes < LargeFileThresholdBytes)
            return _smallFileHandle;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return new ReleaseHandle(_gate);
    }

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
            sem?.Release();
        }
    }
}
