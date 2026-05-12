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
    // Global\ scopes the mutex across user sessions on Windows. On Linux /
    // macOS .NET maps it to a per-runtime POSIX semaphore (prefix ignored
    // but name still gives cross-process isolation).
    internal const string GlobalMutexName = @"Global\ProSoft.CryptoSoft.SingleInstance";

    private readonly CryptoSoftSettings _settings;
    private readonly Mutex _gate;
    private readonly int _lockWaitMs;
    private int _disposed;

    public CryptoSoftAdapter(CryptoSoftSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _gate = new Mutex(initiallyOwned: false, name: GlobalMutexName);

        // Lock wait = 2 × per-file timeout. A queued caller bounded by
        // (holder finishing + own budget). Math.Min on widened long
        // guards against int overflow when an operator sets a huge
        // timeout — without it _lockWaitMs would wrap negative and
        // Mutex.WaitOne would throw ArgumentOutOfRangeException.
        int timeoutMs = _settings.TimeoutMs > 0 ? _settings.TimeoutMs : 30_000;
        _lockWaitMs = (int)Math.Min((long)timeoutMs * 2, int.MaxValue);
    }

    /// <inheritdoc />
    public EncryptResult Encrypt(string source, string dest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(dest);

        // Post-Dispose: the OS mutex handle is closed; WaitOne would
        // throw ObjectDisposedException. Return soft Failed so the
        // caller's fall-back (plain copy, no encryption) covers both
        // Dispose and empty-path cases uniformly.
        if (Volatile.Read(ref _disposed) != 0)
        {
            return EncryptResult.Failed();
        }

        if (string.IsNullOrWhiteSpace(_settings.Path))
        {
            // CryptoSoft not deployed → caller falls back to plain copy.
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

    private bool AcquireGate()
    {
        try
        {
            return _gate.WaitOne(_lockWaitMs);
        }
        catch (AbandonedMutexException)
        {
            // Previous holder crashed without releasing — the OS grants
            // the mutex to us. CryptoSoft itself handles partial-output
            // cleanup on the next launch.
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
            // Standard streams NOT redirected: the contract
            // (docs/cryptosoft-integration.md) communicates only via the
            // exit code. Redirecting without draining the OS pipes would
            // deadlock CryptoSoft past the pipe buffer (~64 KB).
        };
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(dest);

        try
        {
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
