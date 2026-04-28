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

    [Fact]
    public void Encrypt_TrueCommandUnix_ReturnsZeroMs()
    {
        // /bin/true exits with code 0 immediately and ignores arguments. Lets
        // us verify that the adapter parses exit code 0 as Succeeded(0).
        // Skipped on Windows where there is no equivalent built-in.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var adapter = new CryptoSoftAdapter(new CryptoSoftSettings
        {
            Path = "/usr/bin/true",
            TimeoutMs = 5000,
        });

        var result = adapter.Encrypt("ignored-src", "ignored-dst");

        Assert.True(result.Success);
        Assert.Equal(0, result.EncryptionTimeMs);
    }
}
