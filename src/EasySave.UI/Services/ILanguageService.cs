namespace EasySave.UI.Services;

/// <summary>
/// Contract for runtime locale switching and key-based translation lookup.
/// </summary>
public interface ILanguageService
{
    /// <summary>Returns the translated string for <paramref name="key"/> in the active locale.</summary>
    string this[string key] { get; }

    /// <summary>Switches the active locale. Accepted values: "fr", "en".</summary>
    void SetLanguage(string locale);

    /// <summary>Raised after <see cref="SetLanguage"/> completes so bindings can refresh.</summary>
    event EventHandler LanguageChanged;
}
