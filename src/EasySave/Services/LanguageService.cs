using System.Text.Json;

namespace EasySave.Services;

/// <summary>Provides translated UI strings loaded from Resources/{lang}.json at runtime.</summary>
public sealed class LanguageService
{
    private Dictionary<string, string> _translations;

    public LanguageService(AppConfig config)
    {
        // Try the configured language first; fall back to "en" if it fails so
        // the UI never starts up showing raw translation keys when fr.json is
        // missing or corrupt. Last resort is an empty dictionary — T(key)
        // then returns the key, which is at least a debuggable signal.
        if (TryLoad(config.Language, out var translations) ||
            (config.Language != "en" && TryLoad("en", out translations)))
        {
            _translations = translations;
            return;
        }
        _translations = new Dictionary<string, string>();
    }

    /// <summary>Returns the translated string for the given key, or the key itself if not found.</summary>
    public string T(string key) =>
        _translations.TryGetValue(key, out var value) ? value : key;

    /// <summary>
    /// Switches the active language and reloads translations from disk.
    /// Returns <c>true</c> on success. On failure (file missing, malformed
    /// JSON, transient IO error) the previous translations are kept so the
    /// caller can surface a localized error in the *current* language and
    /// the UI does not regress to raw translation keys.
    /// </summary>
    public bool SetLanguage(string lang)
    {
        if (!TryLoad(lang, out var translations))
            return false;

        _translations = translations;
        return true;
    }

    // True only when the file exists, parses, and yields a non-empty dictionary.
    // IOException / UnauthorizedAccessException are treated as transient load
    // failures (same convention as AppConfig.Load in v1.0.1) — the caller
    // decides whether to retry or fall back, which is why this method does
    // not throw.
    private static bool TryLoad(string lang, out Dictionary<string, string> translations)
    {
        translations = new Dictionary<string, string>();

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", $"{lang}.json");
        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (parsed is null || parsed.Count == 0)
                return false;

            translations = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
