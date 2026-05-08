using System.Text.Json;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class LanguageServiceTests : IDisposable
{
    private readonly string _resourcesDir;
    private readonly string _tempDir;
    private readonly List<string> _createdLangFiles = new();

    public LanguageServiceTests()
    {
        _resourcesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
        Directory.CreateDirectory(_resourcesDir);

        _tempDir = Path.Combine(Path.GetTempPath(), $"easysave-lang-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var f in _createdLangFiles)
        {
            try { File.Delete(f); } catch { /* best-effort cleanup */ }
        }
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void SetLanguage_KnownLang_ReturnsTrueAndUpdatesTranslations()
    {
        // Use a synthetic locale code so the test does not depend on the
        // shipped en.json / fr.json content evolving.
        string langCode = "xx-good";
        WriteLangFile(langCode, """{"hello": "world"}""");
        var svc = new LanguageService(MakeConfig("en"));

        Assert.True(svc.SetLanguage(langCode));
        Assert.Equal("world", svc.T("hello"));
    }

    [Fact]
    public void SetLanguage_MissingFile_ReturnsFalse_AndKeepsPreviousTranslations()
    {
        // Issue B1/B2: previously this regressed the UI to raw keys after
        // confirming the language change. The current contract is: keep the
        // previous translations and signal failure to the caller.
        var svc = new LanguageService(MakeConfig("en"));
        string before = svc.T("menu.title");

        bool result = svc.SetLanguage("xx-missing-locale");

        Assert.False(result);
        Assert.Equal(before, svc.T("menu.title"));
    }

    [Fact]
    public void SetLanguage_CorruptJson_ReturnsFalse_AndKeepsPreviousTranslations()
    {
        string langCode = "xx-corrupt";
        WriteLangFile(langCode, "{ this is not valid json");
        var svc = new LanguageService(MakeConfig("en"));
        string before = svc.T("menu.title");

        Assert.False(svc.SetLanguage(langCode));
        Assert.Equal(before, svc.T("menu.title"));
    }

    [Fact]
    public void SetLanguage_EmptyJson_ReturnsFalse()
    {
        // An empty {} dictionary has no translations to switch to — would
        // surface every UI string as a raw key. Reject upfront.
        string langCode = "xx-empty";
        WriteLangFile(langCode, "{}");
        var svc = new LanguageService(MakeConfig("en"));

        Assert.False(svc.SetLanguage(langCode));
    }

    [Fact]
    public void Constructor_PreferredLangMissing_FallsBackToEnglish()
    {
        // Operator misconfigures appsettings.json with a missing language
        // file. The startup must not regress to raw keys; it falls back to
        // the shipped en.json so the menu is still usable.
        var svc = new LanguageService(MakeConfig("xx-missing-startup"));

        Assert.Equal("=== EasySave ===", svc.T("menu.title"));
    }

    private void WriteLangFile(string lang, string content)
    {
        string path = Path.Combine(_resourcesDir, $"{lang}.json");
        File.WriteAllText(path, content);
        _createdLangFiles.Add(path);
    }

    private AppConfig MakeConfig(string language)
    {
        // AppConfig setters are init-only; round-trip a JSON payload through
        // AppConfig.Load to populate the singleton.
        string configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { Language = language };
        File.WriteAllText(configPath, JsonSerializer.Serialize(payload));
        AppConfig.Load(configPath);
        return AppConfig.Instance;
    }
}
