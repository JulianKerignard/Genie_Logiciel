using System.ComponentModel;
using System.Diagnostics;
using EasySave.Models;

namespace EasySave.Services;

/// <summary>
/// Production <see cref="IEncryptionService"/> backed by the external
/// CryptoSoft executable. The full integration contract (CLI arguments,
/// exit-code semantics, single-instance constraint, error handling) lives
/// in <c>docs/cryptosoft-integration.md</c>.
/// </summary>
/// <remarks>
/// CdC v3 enforces that CryptoSoft is Mono-Instance: "il ne peut être
/// exécuté en simultanée sur un même ordinateur". The adapter therefore
/// acquires a named system Mutex before launching the subprocess and
/// releases it once the child has exited. Any concurrent invocation —
/// parallel jobs inside the same EasySave process, or a second EasySave
/// process on the same workstation — serializes on this gate.
/// </remarks>
public sealed class CryptoSoftAdapter : IEncryptionService, IDisposable
{
    // The Global\ prefix scopes the mutex across user sessions on
    // Windows. On Linux / macOS, .NET maps named mutexes to per-runtime
    // POSIX semaphores under /tmp/.dotnet/shm/ — the prefix is ignored
    // but the name still gives cross-process isolation within the
    // ProSoft installation, which is good enough for the dev path.
    private const string GlobalMutexName = @"Global\ProSoft.CryptoSoft.SingleInstance";

    private readonly CryptoSoftSettings _settings;
    private readonly Mutex _gate;
    private readonly int _lockWaitMs;
    private int _disposed;

    public CryptoSoftAdapter(CryptoSoftSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        // Mutex(initiallyOwned: false, name) creates the named mutex or
        // opens the existing one. Multiple CryptoSoftAdapter instances
        // (one per BackupManager / per process) all share the same OS
        // handle by name.
        _gate = new Mutex(initiallyOwned: false, name: GlobalMutexName);

        // A queued caller waits at most 2× the per-file budget for the
        // gate. Predictable upper bound makes operator triage easier:
        // a single queued encryption is bounded by 2 × timeout_ms, which
        // accommodates the worst case of the holder running until its
        // own timeout fires plus the new caller's own encryption budget.
        // If the lock is contended longer, the file is dropped as Failed
        // and logged — operators see the conflict in the daily log
        // instead of an indefinite hang.
        //
        // Math.Min on a widened long guards against int overflow when an
        // operator sets a huge timeout (>1 billion ms ≈ 11 days). Without
        // the widening, _lockWaitMs would wrap to a negative value and
        // Mutex.WaitOne would throw ArgumentOutOfRangeException at the
        // first call.
        int timeoutMs = _settings.TimeoutMs > 0 ? _settings.TimeoutMs : 30_000;
        _lockWaitMs = (int)Math.Min((long)timeoutMs * 2, int.MaxValue);
    }

    /// <inheritdoc />
    public EncryptResult Encrypt(string source, string dest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(dest);

        // Late-arriving call after Dispose(): the OS mutex handle is
        // already closed, so AcquireGate would throw ObjectDisposedException
        // on WaitOne. Convert to a soft Failed instead — same shape as the
        // empty-path fall-back below, so the caller's fall-back path (plain
        // copy, no encryption) handles both cases uniformly.
        if (Volatile.Read(ref _disposed) != 0)
        {
            return EncryptResult.Failed();
        }

        if (string.IsNullOrWhiteSpace(_settings.Path))
        {
            // CryptoSoft not deployed on this workstation. The caller is
            // expected to fall back to a plain copy (no encryption).
            return EncryptResult.Failed();
        }

        bool acquired = false;
        try
        {
            acquired = AcquireGate();
            if (!acquired)
            {
                Trace.TraceWarning(
                    $"[CryptoSoft] Mono-Instance lock contention timeout ({_lockWaitMs} ms) " +
                    $"on '{source}'. Another CryptoSoft invocation held the gate too long. " +
                    $"File dropped from encryption (operator must retry or raise crypto_soft.timeout_ms).");
                return EncryptResult.Failed();
            }

            return RunCryptoSoftProcess(source, dest);
        }
        finally
        {
            if (acquired) _gate.ReleaseMutex();
        }
    }

    // Splits the Mutex acquisition from the catch path so the
    // AbandonedMutexException handling stays local. AbandonedMutex means
    // the previous holder died without releasing — the OS still grants
    // the mutex to the next waiter, and behaving as if we acquired
    // normally is the documented contract.
    private bool AcquireGate()
    {
        try
        {
            return _gate.WaitOne(_lockWaitMs);
        }
        catch (AbandonedMutexException)
        {
            // Previous holder crashed. CryptoSoft itself is responsible
            // for cleaning up its own partial output on the next launch;
            // EasySave just continues.
            return true;
        }
    }

    private EncryptResult RunCryptoSoftProcess(string source, string dest)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _settings.Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Standard streams are intentionally NOT redirected: the contract
            // (docs/cryptosoft-integration.md) communicates only via the exit
            // code. Redirecting without draining the OS pipes would deadlock
            // CryptoSoft as soon as it writes past the pipe buffer (~64 KB).
        };
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(dest);

        try
        {
            // Process.Start with UseShellExecute=false returns a live Process
            // or throws — it never returns null. The null-forgiving operator
            // documents that contract for static analysis.
            using var process = Process.Start(psi)!;

            int timeoutMs = _settings.TimeoutMs > 0 ? _settings.TimeoutMs : 30_000;
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Process already exited or kill denied; nothing else to do.
                }
                return EncryptResult.Failed();
            }

            int exitCode = process.ExitCode;
            return exitCode >= 0
                ? EncryptResult.Succeeded(exitCode)
                : EncryptResult.Failed(exitCode);
        }
        catch (Exception ex) when (ex is FileNotFoundException
                                      or Win32Exception
                                      or InvalidOperationException)
        {
            // FileNotFound: CryptoSoftPath points nowhere.
            // Win32Exception: OS denied the launch.
            // InvalidOperationException: ProcessStartInfo state.
            // None of these should crash the backup job — log a failure and move on.
            return EncryptResult.Failed();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gate.Dispose();
    }
}
