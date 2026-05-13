using System.Diagnostics;

namespace CryptoSoft;

/// <summary>
/// Orchestrates a single CryptoSoft encryption invocation: acquire the
/// mono-instance gate, read the source, run the algorithm, write the
/// target, return the elapsed time. Pure orchestration — IO and crypto
/// knowledge live in <see cref="ICryptoAlgorithm"/> and
/// <see cref="IMonoInstanceGate"/> respectively, so the orchestrator
/// stays unit-testable with fakes.
/// </summary>
internal sealed class CryptoSoftRunner
{
    private readonly ICryptoAlgorithm _algorithm;
    private readonly IMonoInstanceGate _gate;

    public CryptoSoftRunner(ICryptoAlgorithm algorithm, IMonoInstanceGate gate)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(gate);
        _algorithm = algorithm;
        _gate = gate;
    }

    /// <summary>
    /// Runs the encryption end-to-end. Returns an exit code per the
    /// EasySave integration contract: non-negative = elapsed ms on
    /// success; negative values are reserved error codes from
    /// <see cref="ExitCodes"/>.
    /// </summary>
    public int Run(string sourcePath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!File.Exists(sourcePath)) return ExitCodes.SourceNotFound;
        if (!_gate.TryAcquire()) return ExitCodes.AlreadyRunning;

        try
        {
            var sw = Stopwatch.StartNew();
            var plain = File.ReadAllBytes(sourcePath);
            var encrypted = _algorithm.Transform(plain);
            File.WriteAllBytes(targetPath, encrypted);
            sw.Stop();
            // Clamp at int.MaxValue so the exit code stays in range
            // even on pathologically slow runs (would still beat the
            // EasySave timeout long before this clamp fires).
            return (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue);
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException)
        {
            return ExitCodes.IoFailure;
        }
        finally
        {
            _gate.Release();
        }
    }
}
