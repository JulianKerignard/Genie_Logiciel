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
    public void Load_MissingFile_KeepsSettingsDefaults()
    {
        var missing = Path.Combine(_tempDir, "no-settings.json");

        AppConfig.Load(missing);

        Assert.Empty(AppConfig.Instance.Settings.EncryptedExtensions);
        Assert.Empty(AppConfig.Instance.Settings.BusinessSoftwareList);
        Assert.Equal("json", AppConfig.Instance.Settings.LogFormat);
        Assert.Equal(string.Empty, AppConfig.Instance.Settings.CryptoSoft.Path);
    }

    [Fact]
    public void Load_SettingsProvided_BindsAppSettings()
    {
        var file = Path.Combine(_tempDir, "settings.json");
        var payload = """
        {
            "Language": "fr",
            "encrypted_extensions": [".pdf", ".docx"],
            "business_software_list": ["calc.exe", "notepad.exe"],
            "log_format": "xml",
            "crypto_soft": { "path": "/opt/cryptosoft/CryptoSoft.exe" }
        }
        """;
        File.WriteAllText(file, payload);

        AppConfig.Load(file);

        Assert.Equal("fr", AppConfig.Instance.Language);
        Assert.Equal(new[] { ".pdf", ".docx" }, AppConfig.Instance.Settings.EncryptedExtensions);
        Assert.Equal(new[] { "calc.exe", "notepad.exe" }, AppConfig.Instance.Settings.BusinessSoftwareList);
        Assert.Equal("xml", AppConfig.Instance.Settings.LogFormat);
        Assert.Equal("/opt/cryptosoft/CryptoSoft.exe", AppConfig.Instance.Settings.CryptoSoft.Path);
    }
}
