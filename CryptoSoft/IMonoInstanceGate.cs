namespace CryptoSoft;

/// <summary>
/// Cross-process mono-instance gate. The cahier V3 mandates that CryptoSoft
/// "ne peut être exécuté en simultanée sur un même ordinateur"; the
/// implementation must therefore own a machine-wide handle (named system
/// Mutex, file lock, etc.) that another CryptoSoft process can probe.
/// </summary>
internal interface IMonoInstanceGate : IDisposable
{
    /// <summary>
    /// Non-blocking probe-and-acquire. Returns <c>true</c> when the
    /// caller now owns the gate, <c>false</c> when another process holds
    /// it. Implementations must NOT queue / wait — CryptoSoft prefers a
    /// hard refusal so EasySave can drop the file from the current run
    /// and continue its parallel pipeline.
    /// </summary>
    bool TryAcquire();

    /// <summary>
    /// Releases the gate if this instance currently holds it. No-op
    /// otherwise. Safe to call multiple times.
    /// </summary>
    void Release();
}
