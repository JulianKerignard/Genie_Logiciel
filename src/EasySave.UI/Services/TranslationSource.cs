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

    /// <summary>
    /// Wires the language service. Idempotent — subsequent calls are no-ops so
    /// a second <c>Initialize</c> call never stacks duplicate subscribers.
    /// </summary>
    public void Initialize(ILanguageService service)
    {
        if (_service is not null)
            return;

        _service = service;
        _service.LanguageChanged += (_, _) =>
        {
            // Notify Avalonia's binding engine through every channel its
            // IndexerNode.Listener.OnPropertyChanged accepts: a null name,
            // the empty string (string.IsNullOrEmpty short-circuit), and
            // the WPF-style "Item[]" sentinel. Some indexer bindings
            // (notably the ones built by sidebar Button > TextBlock with
            // a custom ControlTheme) only refresh on one of those, so we
            // emit all three to be safe across Avalonia versions.
            var handler = PropertyChanged;
            if (handler is null) return;
            handler(this, new PropertyChangedEventArgs(null));
            handler(this, new PropertyChangedEventArgs(string.Empty));
            handler(this, new PropertyChangedEventArgs("Item[]"));
        };
    }

    /// <summary>Returns the translation for <paramref name="key"/> in the active locale.</summary>
    public string this[string key] => _service?[key] ?? key;
}
