using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class AppSettingsLiveFileTests
{
    [Fact]
    public void Load_RealAppsettingsJson_BindsAllSettingsKeys()
    {
        var repoRoot = FindRepoRoot();
        var liveFile = Path.Combine(repoRoot, "src", "EasySave", "appsettings.json");

        AppConfig.Load(liveFile);

        Assert.Contains(".pdf", AppConfig.Instance.Settings.EncryptedExtensions);
        Assert.Contains(".docx", AppConfig.Instance.Settings.EncryptedExtensions);
        Assert.Contains(".xlsx", AppConfig.Instance.Settings.EncryptedExtensions);
        Assert.Contains("calc.exe", AppConfig.Instance.Settings.BusinessSoftware);
        Assert.Contains("notepad.exe", AppConfig.Instance.Settings.BusinessSoftware);
        Assert.Equal("json", AppConfig.Instance.Settings.LogFormat);
        Assert.NotNull(AppConfig.Instance.Settings.CryptoSoft);
        // Sanity-check the CryptoSoft sub-section keys documented in
        // docs/cryptosoft-integration.md so the doc and the live appsettings
        // cannot drift apart silently.
        Assert.Equal(string.Empty, AppConfig.Instance.Settings.CryptoSoft.Path);
        Assert.Equal(30000, AppConfig.Instance.Settings.CryptoSoft.TimeoutMs);
        Assert.Equal(4096, AppConfig.Instance.Settings.LargeFileThresholdKb);
        Assert.False(AppConfig.Instance.Settings.RemoteConsoleEnabled);
        Assert.Equal(9000, AppConfig.Instance.Settings.RemoteConsolePort);
        Assert.Equal(4, AppConfig.Instance.Settings.MaxParallelJobs);
    }

    [Fact]
    public void Load_V2SettingsJson_UsesV3Defaults_WhenKeysAbsent()
    {
        // A V2 settings.json has no V3 keys — missing keys must silently fall back
        // to defaults without throwing or corrupting the instance.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "language": "fr",
                "encrypted_extensions": [".pdf"],
                "business_software": [],
                "log_format": "json",
                "crypto_soft": { "path": "", "timeout_ms": 30000 }
            }
            """);

            AppConfig.Load(tempFile);

            Assert.Equal("fr", AppConfig.Instance.Settings.Language);
            Assert.Equal(4096, AppConfig.Instance.Settings.LargeFileThresholdKb);
            Assert.False(AppConfig.Instance.Settings.RemoteConsoleEnabled);
            Assert.Equal(9000, AppConfig.Instance.Settings.RemoteConsolePort);
            Assert.Equal(4, AppConfig.Instance.Settings.MaxParallelJobs);
        }
        finally
        {
            File.Delete(tempFile);
            var liveFile = Path.Combine(FindRepoRoot(), "src", "EasySave", "appsettings.json");
            AppConfig.Load(liveFile);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasySave.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
