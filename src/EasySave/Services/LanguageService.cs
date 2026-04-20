namespace EasySave.Services;

/// <summary>Provides translated UI strings. Stub: returns the key as-is until file loading is implemented.</summary>
public sealed class LanguageService
{
    public LanguageService(AppConfig config) { }

    /// <summary>Returns the UI string for the given key, or the key itself if not found.</summary>
    public string T(string key) => key;
}
