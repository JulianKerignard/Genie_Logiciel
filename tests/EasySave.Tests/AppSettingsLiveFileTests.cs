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
