using Avalonia.Platform;
using System.Text.Json;

namespace EasySave.UI.Services;

/// <summary>
/// Loads translations from embedded Avalonia assets and dispatches locale changes.
/// Defaults to French on startup.
/// </summary>
public sealed class LanguageService : ILanguageService
{
    private Dictionary<string, string> _translations = new();

    public event EventHandler? LanguageChanged;

    public LanguageService() => LoadLocale("fr");

    /// <inheritdoc/>
    public string this[string key] =>
        _translations.TryGetValue(key, out var value) ? value : key;

    /// <inheritdoc/>
    public void SetLanguage(string locale)
    {
        LoadLocale(locale);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadLocale(string locale)
    {
        var uri = new Uri($"avares://EasySave.UI/Assets/i18n/{locale}.json");
        try
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                            ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException
                                       or JsonException or IOException)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[LanguageService] Failed to load locale '{locale}': {ex.Message}");
            _translations = new Dictionary<string, string>();
        }
    }
}
