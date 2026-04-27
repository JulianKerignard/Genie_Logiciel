using System.Text.Json;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

// Each test gets its own temp directory and reloads AppConfig so SettingsRepository
// reads/writes an isolated settings.json.
[Collection("StateCollection")]
public class SettingsRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFilePath;

    public SettingsRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsFilePath = Path.Combine(_tempDir, "settings.json");

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { SettingsFilePath = _settingsFilePath };
        File.WriteAllText(configPath, JsonSerializer.Serialize(payload));

        AppConfig.Load(configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        Assert.False(File.Exists(_settingsFilePath));

        var settings = SettingsRepository.Instance.Load();

        Assert.Empty(settings.EncryptedExtensions);
        Assert.Empty(settings.BusinessSoftware);
        Assert.Equal("en", settings.Language);
        Assert.Equal("json", settings.LogFormat);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip()
    {
        var saved = new GeneralSettings
        {
            EncryptedExtensions = new[] { ".pdf", ".docx" },
            BusinessSoftware = new[] { "calc.exe" },
            Language = "fr",
            LogFormat = "xml"
        };

        SettingsRepository.Instance.Save(saved);
        var loaded = SettingsRepository.Instance.Load();

        Assert.Equal(new[] { ".pdf", ".docx" }, loaded.EncryptedExtensions);
        Assert.Equal(new[] { "calc.exe" }, loaded.BusinessSoftware);
        Assert.Equal("fr", loaded.Language);
        Assert.Equal("xml", loaded.LogFormat);
    }

    [Fact]
    public void Save_RaisesSettingsChanged()
    {
        GeneralSettings? received = null;
        EventHandler<GeneralSettings> handler = (_, s) => received = s;
        SettingsRepository.Instance.SettingsChanged += handler;
        try
        {
            var settings = new GeneralSettings { Language = "fr" };
            SettingsRepository.Instance.Save(settings);
        }
        finally
        {
            SettingsRepository.Instance.SettingsChanged -= handler;
        }

        Assert.NotNull(received);
        Assert.Equal("fr", received!.Language);
    }

    [Fact]
    public void Save_LeavesNoTempFiles()
    {
        SettingsRepository.Instance.Save(new GeneralSettings { Language = "fr" });

        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void Load_CorruptedFile_QuarantinesAndReturnsDefaults()
    {
        File.WriteAllText(_settingsFilePath, "{ not valid json");

        var settings = SettingsRepository.Instance.Load();

        Assert.Equal("en", settings.Language);
        Assert.NotEmpty(Directory.GetFiles(_tempDir, "*.corrupted-*"));
    }
}
