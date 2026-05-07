using EasySave.Services;

namespace EasySave.Tests;

public class ThrottledEncryptionServiceTests
{
    // Fake that records peak concurrency and optionally blocks until released.
    private sealed class RecordingEncryptionService : IEncryptionService
    {
        private int _current;
        public int PeakConcurrent;
        public ManualResetEventSlim? Gate;

        public EncryptResult Encrypt(string source, string dest)
        {
            var c = Interlocked.Increment(ref _current);
            Interlocked.Exchange(ref PeakConcurrent, Math.Max(PeakConcurrent, c));
            Gate?.Wait();
            Interlocked.Decrement(ref _current);
            return EncryptResult.Succeeded(0);
        }
    }

    [Fact]
    public void Encrypt_PassesResult_FromInnerService()
    {
        var inner = new RecordingEncryptionService();
        using var sut = new ThrottledEncryptionService(inner);

        var result = sut.Encrypt("src", "dst");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Encrypt_WithMaxConcurrent1_NeverRunsMoreThanOneAtATime()
    {
        var gate = new ManualResetEventSlim(false);
        var inner = new RecordingEncryptionService { Gate = gate };
        using var sut = new ThrottledEncryptionService(inner, maxConcurrent: 1);

        // Launch two concurrent encrypt calls; the semaphore must serialize them.
        var t1 = Task.Run(() => sut.Encrypt("a", "a"));
        // Give t1 time to enter the semaphore and block on the gate.
        await Task.Delay(50);
        var t2 = Task.Run(() => sut.Encrypt("b", "b"));

        // Release the gate — both calls will eventually complete.
        gate.Set();
        await Task.WhenAll(t1, t2);

        // Peak must be 1: the second call was queued, never overlapping.
        Assert.Equal(1, inner.PeakConcurrent);
    }

    [Fact]
    public async Task Encrypt_WithMaxConcurrent2_AllowsTwoConcurrentCalls()
    {
        var gate = new ManualResetEventSlim(false);
        var inner = new RecordingEncryptionService { Gate = gate };
        using var sut = new ThrottledEncryptionService(inner, maxConcurrent: 2);

        var t1 = Task.Run(() => sut.Encrypt("a", "a"));
        var t2 = Task.Run(() => sut.Encrypt("b", "b"));
        await Task.Delay(80);

        gate.Set();
        await Task.WhenAll(t1, t2);

        // Both calls must have overlapped.
        Assert.Equal(2, inner.PeakConcurrent);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenInnerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ThrottledEncryptionService(null!));
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenMaxConcurrentIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ThrottledEncryptionService(new RecordingEncryptionService(), 0));
    }
}
