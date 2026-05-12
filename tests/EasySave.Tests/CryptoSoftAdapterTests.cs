using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

// Unit tests for the validation and error paths of CryptoSoftAdapter. The
// success path (Process.Start + exit-code parsing against a real CryptoSoft)
// is exercised end-to-end via the BackupManager integration tests, which
// inject a controllable IEncryptionService instead of spinning up a fake exe
// per test.
public class CryptoSoftAdapterTests
{
    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CryptoSoftAdapter(null!));
    }

    [Fact]
    public void Encrypt_EmptyPath_ReturnsFailure()
    {
        using var adapter = new CryptoSoftAdapter(new CryptoSoftSettings { Path = string.Empty });

        var result = adapter.Encrypt("/tmp/src", "/tmp/dst");

        Assert.False(result.Success);
        Assert.Equal(-1, result.EncryptionTimeMs);
    }

    [Fact]
    public void Encrypt_PathDoesNotExist_ReturnsFailure()
    {
        using var adapter = new CryptoSoftAdapter(new CryptoSoftSettings
        {
            Path = "/this/path/definitely/does/not/exist/cryptosoft.exe",
            TimeoutMs = 1000,
        });

        var result = adapter.Encrypt("/tmp/src", "/tmp/dst");

        Assert.False(result.Success);
        Assert.True(result.EncryptionTimeMs < 0);
    }

    [Fact]
    public void Encrypt_NullSource_Throws()
    {
        using var adapter = new CryptoSoftAdapter(new CryptoSoftSettings { Path = "anything" });

        // ArgumentException.ThrowIfNullOrWhiteSpace surfaces ArgumentNullException
        // for null and ArgumentException for whitespace; ThrowsAny accepts both.
        Assert.ThrowsAny<ArgumentException>(() => adapter.Encrypt(null!, "/tmp/dst"));
    }

    [Fact]
    public void Encrypt_NullDest_Throws()
    {
        using var adapter = new CryptoSoftAdapter(new CryptoSoftSettings { Path = "anything" });

        Assert.ThrowsAny<ArgumentException>(() => adapter.Encrypt("/tmp/src", null!));
    }

    [SkippableFact]
    public void Encrypt_TrueCommandUnix_ReturnsZeroMs()
    {
        // /bin/true exits with code 0 immediately and ignores arguments. Lets
        // us verify that the adapter parses exit code 0 as Succeeded(0).
        // Reported as Skipped (not Passed) on Windows so test results stay honest.
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            "Requires /usr/bin/true; no built-in Windows equivalent.");

        using var adapter = new CryptoSoftAdapter(new CryptoSoftSettings
        {
            Path = "/usr/bin/true",
            TimeoutMs = 5000,
        });

        var result = adapter.Encrypt("ignored-src", "ignored-dst");

        Assert.True(result.Success);
        Assert.Equal(0, result.EncryptionTimeMs);
    }

    // Writes a tiny POSIX shell script that sleeps for $1 seconds and
    // ignores any subsequent positional args. Used as a fake CryptoSoft
    // binary by the concurrency tests — sleeping is enough to let two
    // adapter.Encrypt calls overlap (or fail to overlap, depending on
    // the mutex contract under test).
    private static string CreateFakeCryptoSoftScript()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "fake-cryptosoft-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(path, "#!/bin/sh\nsleep \"$1\"\n");
        // Owner-rwx; mode bits only meaningful on Unix. The
        // OperatingSystem.IsWindows() guard is what CA1416 needs to
        // statically prove the SetUnixFileMode call is unreachable on
        // Windows — a try/catch (PlatformNotSupportedException) is not
        // enough for the platform-compatibility analyzer.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    [SkippableFact]
    public void Encrypt_TwoConcurrentCalls_AreSerialized_NotParallel()
    {
        // CdC v3 mono-instance contract: two concurrent EasySave callers
        // launching CryptoSoft on the same machine must serialize on the
        // shared Mutex. The fake encryption below sleeps 500 ms. Two
        // parallel calls would complete in ~500 ms; serialized they take
        // ~1 s. The wall time assertion below proves the second caller
        // waited on the gate.
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            "Uses a POSIX shell script as the fake CryptoSoft binary.");

        string scriptPath = CreateFakeCryptoSoftScript();
        try
        {
            using var adapter = new CryptoSoftAdapter(new CryptoSoftSettings
            {
                Path = scriptPath,
                TimeoutMs = 5000,
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t1 = Task.Run(() => adapter.Encrypt("0.5", "ignored-dest"));
            var t2 = Task.Run(() => adapter.Encrypt("0.5", "ignored-dest"));
            Task.WaitAll(t1, t2);
            sw.Stop();

            Assert.True(t1.Result.Success && t2.Result.Success,
                $"Fake encryption itself failed (script launch). " +
                $"t1.Success={t1.Result.Success}, t2.Success={t2.Result.Success}, " +
                $"elapsed={sw.ElapsedMilliseconds}ms.");

            // Two serialized 500 ms sleeps ≥ 1000 ms. Allow 100 ms slack
            // for process spawn overhead being faster than expected.
            Assert.True(sw.ElapsedMilliseconds >= 900,
                $"Concurrent encrypts did NOT serialize: {sw.ElapsedMilliseconds} ms " +
                $"for two 500 ms sleeps. Mutex contract broken.");
            Assert.True(sw.ElapsedMilliseconds < 2500,
                $"Serialized encrypts took too long: {sw.ElapsedMilliseconds} ms — " +
                $"a queued caller may be hung on the mutex.");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Encrypt_LockTimeout_ReturnsFailed()
    {
        // Hold the named mutex from a DEDICATED thread, then call
        // adapter.Encrypt on the test thread. Named mutexes are
        // recursive per thread — holding from the same thread that
        // later calls Encrypt would let the recursive WaitOne return
        // immediately and falsely report "no contention". A dedicated
        // owner thread makes the contention real.
        string mutexName = CryptoSoftAdapter.GlobalMutexName;
        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        // Use a dedicated Thread (not Task.Run) because Mutex thread-
        // affinity is OS-level: ReleaseMutex must run on the same
        // thread that called WaitOne. Task.Run gives no such guarantee.
        var holder = new Thread(() =>
        {
            using var gate = new Mutex(initiallyOwned: false, name: mutexName);
            if (!gate.WaitOne(TimeSpan.FromSeconds(2)))
            {
                acquired.Set();
                return;
            }
            try
            {
                acquired.Set();
                release.Wait();
            }
            finally
            {
                gate.ReleaseMutex();
            }
        })
        { IsBackground = true };
        holder.Start();

        try
        {
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(2)),
                "Holder thread never signalled mutex acquisition.");

            // Lock wait = 2 × TimeoutMs, so TimeoutMs = 300 → wait = 600 ms.
            using var waiter = new CryptoSoftAdapter(new CryptoSoftSettings
            {
                // Path can be anything — the adapter never reaches
                // Process.Start because the mutex acquisition times out
                // first.
                Path = "/dev/null",
                TimeoutMs = 300,
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var waiterResult = waiter.Encrypt("ignored-src", "ignored-dest");
            sw.Stop();

            Assert.False(waiterResult.Success,
                "Waiter should have failed on the mono-instance lock timeout.");
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Waiter took {sw.ElapsedMilliseconds} ms — should have bailed at ~600 ms.");
            Assert.True(sw.ElapsedMilliseconds >= 500,
                $"Waiter returned in {sw.ElapsedMilliseconds} ms — the lock-wait budget " +
                "(600 ms) appears to not have been honored.");
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(2));
        }
    }
}
