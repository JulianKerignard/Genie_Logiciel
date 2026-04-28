using System.ComponentModel;

namespace EasySave.UI.Services;

/// <summary>
/// Singleton INPC bridge between <see cref="ILanguageService"/> and XAML bindings.
/// When the language changes, raises <see cref="PropertyChanged"/> with the indexer
/// name so every <c>{markup:T}</c> binding refreshes automatically.
/// </summary>
public sealed class TranslationSource : INotifyPropertyChanged
{
    /// <summary>The single shared instance wired up from <c>App.axaml.cs</c>.</summary>
    public static readonly TranslationSource Instance = new();

    private ILanguageService? _service;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Wires the language service. Must be called once at startup.</summary>
    public void Initialize(ILanguageService service)
    {
        _service = service;
        _service.LanguageChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Returns the translation for <paramref name="key"/> in the active locale.</summary>
    public string this[string key] => _service?[key] ?? key;
}
