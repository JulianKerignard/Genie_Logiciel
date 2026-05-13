using System.Diagnostics;

namespace CryptoSoft;

/// <summary>
/// Cross-process gate backed by a named system <see cref="Mutex"/>. The
/// <c>Global\</c> prefix scopes the mutex across user sessions on
/// Windows; on Linux / macOS .NET maps it to a per-runtime POSIX
/// semaphore — the prefix is ignored but the name still gives
/// cross-process isolation.
/// </summary>
internal sealed class SystemMutexGate : IMonoInstanceGate
{
    /// <summary>
    /// Stable machine-wide identifier. Must NOT collide with the
    /// defensive serialization mutex used by EasySave's adapter (see
    /// <c>CryptoSoftAdapter.GlobalMutexName</c>) — the two layers are
    /// intentionally independent so both can fire without dead-locking
    /// each other.
    /// </summary>
    internal const string MutexName = @"Global\ProSoft.CryptoSoft.SingleInstance";

    private readonly Mutex _mutex;
    private bool _heldByUs;
    private bool _disposed;

    public SystemMutexGate() : this(MutexName) { }

    /// <summary>
    /// Test seam: lets unit tests pass an isolated, per-run mutex name so
    /// they never collide with a CryptoSoft binary running on the same
    /// machine (a shared CI agent would otherwise flake on the production
    /// name). Production code uses the parameterless constructor.
    /// </summary>
    internal SystemMutexGate(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        _mutex = new Mutex(initiallyOwned: false, name: mutexName);
    }

    public bool TryAcquire()
    {
        if (_disposed) return false;
        try
        {
            // Zero timeout = polled non-blocking probe. See the interface
            // contract for the rationale (hard refusal vs queuing).
            _heldByUs = _mutex.WaitOne(TimeSpan.Zero);
            return _heldByUs;
        }
        catch (AbandonedMutexException)
        {
            // A previous holder crashed without releasing — the OS hands
            // us the mutex. Behave as if we acquired cleanly so the
            // current run can proceed.
            _heldByUs = true;
            return true;
        }
    }

    public void Release()
    {
        if (!_heldByUs || _disposed) return;
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException ex)
        {
            // Foreign thread tried to release — log and move on; the OS
            // will reclaim the mutex on process exit.
            Trace.TraceWarning($"[CryptoSoft] Mutex release failed: {ex.Message}");
        }
        _heldByUs = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Release();
        _mutex.Dispose();
        _disposed = true;
    }
}
