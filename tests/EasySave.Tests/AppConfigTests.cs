using System.Text.Json;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class AppConfigTests : IDisposable
{
    private readonly string _tempDir;

    public AppConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "appconfig-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_KeepsDefaults()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.json");

        AppConfig.Load(missing);

        Assert.NotNull(AppConfig.Instance);
        Assert.Equal("en", AppConfig.Instance.Language);
    }

    [Fact]
    public void Load_ValidFile_AppliesValues()
    {
        var file = Path.Combine(_tempDir, "settings.json");
        var payload = new
        {
            LogDirectory = "/tmp/custom-logs",
            StateFilePath = "/tmp/custom-state.json",
            JobsFilePath = "/tmp/custom-jobs.json",
            Language = "fr"
        };
        File.WriteAllText(file, JsonSerializer.Serialize(payload));

        AppConfig.Load(file);

        Assert.Equal("/tmp/custom-logs", AppConfig.Instance.LogDirectory);
        Assert.Equal("/tmp/custom-state.json", AppConfig.Instance.StateFilePath);
        Assert.Equal("/tmp/custom-jobs.json", AppConfig.Instance.JobsFilePath);
        Assert.Equal("fr", AppConfig.Instance.Language);
    }

    [Fact]
    public void Load_CorruptedFile_FallsBackToDefaults()
    {
        var file = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(file, "{ not valid json");

        AppConfig.Load(file);

        Assert.Equal("en", AppConfig.Instance.Language);
    }

    [Fact]
    public void Load_MissingCryptoKeys_KeepsCryptoDefaults()
    {
        var missing = Path.Combine(_tempDir, "no-crypto.json");

        AppConfig.Load(missing);

        Assert.Equal(string.Empty, AppConfig.Instance.CryptoSoftPath);
        Assert.Equal(string.Empty, AppConfig.Instance.CryptoSoftExtensions);
        Assert.Equal(30000, AppConfig.Instance.CryptoSoftTimeoutMs);
    }

    [Fact]
    public void Load_CryptoKeysProvided_RoundTrip()
    {
        var file = Path.Combine(_tempDir, "crypto-settings.json");
        var payload = new
        {
            CryptoSoftPath = "/opt/cryptosoft/CryptoSoft.exe",
            CryptoSoftExtensions = ".docx,.pdf",
            CryptoSoftTimeoutMs = 5000
        };
        File.WriteAllText(file, JsonSerializer.Serialize(payload));

        AppConfig.Load(file);

        Assert.Equal("/opt/cryptosoft/CryptoSoft.exe", AppConfig.Instance.CryptoSoftPath);
        Assert.Equal(".docx,.pdf", AppConfig.Instance.CryptoSoftExtensions);
        Assert.Equal(5000, AppConfig.Instance.CryptoSoftTimeoutMs);
    }
}
