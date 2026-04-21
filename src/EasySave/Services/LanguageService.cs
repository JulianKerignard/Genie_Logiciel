using System.Text.Json;

namespace EasySave.Services;

/// <summary>Provides translated UI strings loaded from Resources/{lang}.json at runtime.</summary>
public sealed class LanguageService
{
    private Dictionary<string, string> _translations;

    public LanguageService(AppConfig config)
    {
        _translations = LoadTranslations(config.Language);
    }

    /// <summary>Returns the translated string for the given key, or the key itself if not found.</summary>
    public string T(string key) =>
        _translations.TryGetValue(key, out var value) ? value : key;

    /// <summary>Switches the active language and reloads translations from disk.</summary>
    public void SetLanguage(string lang)
    {
        _translations = LoadTranslations(lang);
    }

    private static Dictionary<string, string> LoadTranslations(string lang)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", $"{lang}.json");

        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
