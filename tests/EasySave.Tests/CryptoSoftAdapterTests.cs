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
        var adapter = new CryptoSoftAdapter(new CryptoSoftSettings { Path = string.Empty });

        var result = adapter.Encrypt("/tmp/src", "/tmp/dst");

        Assert.False(result.Success);
        Assert.Equal(-1, result.EncryptionTimeMs);
    }

    [Fact]
    public void Encrypt_PathDoesNotExist_ReturnsFailure()
    {
        var adapter = new CryptoSoftAdapter(new CryptoSoftSettings
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
        var adapter = new CryptoSoftAdapter(new CryptoSoftSettings { Path = "anything" });

        // ArgumentException.ThrowIfNullOrWhiteSpace surfaces ArgumentNullException
        // for null and ArgumentException for whitespace; ThrowsAny accepts both.
        Assert.ThrowsAny<ArgumentException>(() => adapter.Encrypt(null!, "/tmp/dst"));
    }

    [Fact]
    public void Encrypt_NullDest_Throws()
    {
        var adapter = new CryptoSoftAdapter(new CryptoSoftSettings { Path = "anything" });

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
        // Owner-rwx; mode bits not portable to Windows but those tests
        // are skipped there anyway.
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        catch (PlatformNotSupportedException) { /* Windows path, irrelevant */ }
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

    [SkippableFact]
    public void Encrypt_LockTimeout_ReturnsFailed()
    {
        // Hold the gate externally with a 1.5 s fake encryption then
        // verify that a second caller with a tighter lock-wait budget
        // bails out as Failed instead of hanging.
        // Lock wait = 2 × TimeoutMs, so TimeoutMs = 300 → wait = 600 ms.
        Skip.IfNot(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
            "Uses a POSIX shell script as the fake CryptoSoft binary.");

        string scriptPath = CreateFakeCryptoSoftScript();
        try
        {
            using var hold = new CryptoSoftAdapter(new CryptoSoftSettings
            {
                Path = scriptPath,
                TimeoutMs = 5000,
            });
            using var waiter = new CryptoSoftAdapter(new CryptoSoftSettings
            {
                Path = scriptPath,
                TimeoutMs = 300,
            });

            var holdTask = Task.Run(() => hold.Encrypt("1.5", "ignored-dest"));

            // Give the holder time to acquire the mutex before the
            // waiter races in.
            Thread.Sleep(150);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var waiterResult = waiter.Encrypt("0.1", "ignored-dest");
            sw.Stop();

            Assert.False(waiterResult.Success,
                "Waiter should have failed on the mono-instance lock timeout.");
            Assert.True(sw.ElapsedMilliseconds < 1200,
                $"Waiter took {sw.ElapsedMilliseconds} ms — should have bailed at ~600 ms.");

            // Let the holder finish so the mutex is released for
            // subsequent tests in the suite.
            holdTask.Wait(TimeSpan.FromSeconds(3));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }
}
