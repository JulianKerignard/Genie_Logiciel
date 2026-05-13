namespace CryptoSoft.Tests;

/// <summary>
/// Smoke test for the production gate. The full cross-process behaviour
/// (a second CryptoSoft binary refusing to start while a first one runs)
/// is exercised end-to-end in <c>docs/recettes/v3-cryptosoft-mono-instance.md</c>
/// — that scenario actually spawns two CryptoSoft processes. Here we only
/// validate the local plumbing (acquire / release / dispose) on a single
/// thread, which is enough to catch regressions in the wrapper itself
/// without depending on platform-specific mutex implementation details
/// across Windows / Linux / macOS.
/// </summary>
public sealed class SystemMutexGateTests
{
    [Fact]
    public void Acquire_Release_Acquire_Cycle_Succeeds()
    {
        using var gate = new SystemMutexGate();

        Assert.True(gate.TryAcquire());
        gate.Release();
        Assert.True(gate.TryAcquire());
        gate.Release();
    }

    [Fact]
    public void Release_WithoutPriorAcquire_IsNoOp()
    {
        using var gate = new SystemMutexGate();

        // Must not throw, even though the gate never owned the mutex.
        gate.Release();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var gate = new SystemMutexGate();
        gate.TryAcquire();

        gate.Dispose();
        gate.Dispose();
    }
}
