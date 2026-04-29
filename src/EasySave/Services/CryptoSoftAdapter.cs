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
public sealed class CryptoSoftAdapter : IEncryptionService
{
    private readonly CryptoSoftSettings _settings;

    public CryptoSoftAdapter(CryptoSoftSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settings.Path);

    /// <inheritdoc />
    public EncryptResult Encrypt(string source, string dest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(dest);

        if (string.IsNullOrWhiteSpace(_settings.Path))
        {
            // CryptoSoft not deployed on this workstation. The caller is
            // expected to fall back to a plain copy (no encryption).
            return EncryptResult.Failed();
        }

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

            var timeoutMs = _settings.TimeoutMs > 0 ? _settings.TimeoutMs : 30000;
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
}
