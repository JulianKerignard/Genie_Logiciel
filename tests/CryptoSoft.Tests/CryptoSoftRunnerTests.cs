namespace CryptoSoft.Tests;

/// <summary>
/// Tests <see cref="CryptoSoftRunner"/> in isolation with fake gates and
/// algorithms — no real Mutex, no real subprocess. The system-Mutex
/// behaviour is covered separately by an integration scenario in the
/// V3 recettes.
/// </summary>
public sealed class CryptoSoftRunnerTests : IDisposable
{
    private readonly string _scratch;

    public CryptoSoftRunnerTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "cryptosoft-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Run_HappyPath_WritesEncryptedBytes_AndReturnsElapsedMs()
    {
        var src = Path.Combine(_scratch, "in.bin");
        var dst = Path.Combine(_scratch, "out.bin");
        File.WriteAllBytes(src, "hello"u8.ToArray());

        var runner = new CryptoSoftRunner(new XorCryptoAlgorithm(0xFF), new AlwaysAcquiringGate());

        int exit = runner.Run(src, dst);

        Assert.True(exit >= 0, $"Expected elapsed ms (>= 0), got {exit}");
        Assert.True(File.Exists(dst));
        // XOR-0xFF on each byte of "hello" → not equal to the original.
        byte[] encrypted = File.ReadAllBytes(dst);
        Assert.NotEqual("hello"u8.ToArray(), encrypted);
    }

    [Fact]
    public void Run_SourceMissing_ReturnsSourceNotFound_AndDoesNotAcquireGate()
    {
        var gate = new AlwaysAcquiringGate();
        var runner = new CryptoSoftRunner(new XorCryptoAlgorithm(0x00), gate);

        int exit = runner.Run(Path.Combine(_scratch, "missing.bin"), Path.Combine(_scratch, "out.bin"));

        Assert.Equal(ExitCodes.SourceNotFound, exit);
        Assert.False(gate.WasAcquired);
    }

    [Fact]
    public void Run_GateRefused_ReturnsAlreadyRunning_AndDoesNotWriteTarget()
    {
        var src = Path.Combine(_scratch, "in.bin");
        var dst = Path.Combine(_scratch, "out.bin");
        File.WriteAllBytes(src, new byte[] { 1 });
        var runner = new CryptoSoftRunner(new XorCryptoAlgorithm(0x00), new AlwaysRefusingGate());

        int exit = runner.Run(src, dst);

        Assert.Equal(ExitCodes.AlreadyRunning, exit);
        Assert.False(File.Exists(dst));
    }

    [Fact]
    public void Run_GateAlwaysReleasedOnSuccess()
    {
        var src = Path.Combine(_scratch, "in.bin");
        var dst = Path.Combine(_scratch, "out.bin");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3 });
        var gate = new AlwaysAcquiringGate();
        var runner = new CryptoSoftRunner(new XorCryptoAlgorithm(0x00), gate);

        runner.Run(src, dst);

        Assert.True(gate.WasReleased);
    }

    [Fact]
    public void Run_GateReleasedEvenWhenIoFails()
    {
        var src = Path.Combine(_scratch, "in.bin");
        File.WriteAllBytes(src, new byte[] { 1 });
        // Target path under a missing parent that we never create AND
        // is sufficiently malformed to make File.WriteAllBytes throw.
        var invalidTarget = Path.Combine(_scratch, "no-such-dir", "out.bin");
        var gate = new AlwaysAcquiringGate();
        var runner = new CryptoSoftRunner(new XorCryptoAlgorithm(0x00), gate);

        int exit = runner.Run(src, invalidTarget);

        Assert.Equal(ExitCodes.IoFailure, exit);
        Assert.True(gate.WasReleased);
    }

    [Theory]
    [InlineData("", "out.bin")]
    [InlineData("in.bin", "")]
    public void Run_BlankPaths_Throw(string source, string target)
    {
        var runner = new CryptoSoftRunner(new XorCryptoAlgorithm(0x00), new AlwaysAcquiringGate());

        Assert.Throws<ArgumentException>(() => runner.Run(source, target));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────

    private sealed class AlwaysAcquiringGate : IMonoInstanceGate
    {
        public bool WasAcquired { get; private set; }
        public bool WasReleased { get; private set; }
        public bool TryAcquire() { WasAcquired = true; return true; }
        public void Release() { WasReleased = true; }
        public void Dispose() { }
    }

    private sealed class AlwaysRefusingGate : IMonoInstanceGate
    {
        public bool TryAcquire() => false;
        public void Release() { }
        public void Dispose() { }
    }
}
