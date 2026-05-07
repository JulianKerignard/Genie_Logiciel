namespace EasySave.Services;

// Decorator that serializes calls to an inner IEncryptionService via a
// SemaphoreSlim. Required when multiple backup jobs run concurrently in V3:
// CryptoSoft is an external process not designed for concurrent invocation —
// a single semaphore slot (default) ensures at most one instance runs at once.
public sealed class ThrottledEncryptionService : IEncryptionService, IDisposable
{
    private readonly IEncryptionService _inner;
    private readonly SemaphoreSlim _semaphore;

    public ThrottledEncryptionService(IEncryptionService inner, int maxConcurrent = 1)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);
        _inner = inner;
        _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public EncryptResult Encrypt(string source, string dest)
    {
        _semaphore.Wait();
        try
        {
            return _inner.Encrypt(source, dest);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
