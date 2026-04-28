using Avalonia.Data;
using Avalonia.Markup.Xaml;
using EasySave.UI.Services;

namespace EasySave.UI.Markup;

/// <summary>
/// XAML markup extension that provides reactive translated strings.
/// Binds to <see cref="TranslationSource.Instance"/> so the UI refreshes
/// automatically when the active locale changes without restarting the app.
/// </summary>
/// <example>
/// <code>&lt;TextBlock Text="{markup:T Key=menu.jobs}" /&gt;</code>
/// </example>
public sealed class TExtension : MarkupExtension
{
    /// <summary>The translation key to look up in the active locale's dictionary.</summary>
    public string Key { get; set; } = string.Empty;

    public TExtension() { }

    public TExtension(string key) => Key = key;

    /// <inheritdoc/>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Returns a reflection-based Binding so the target property refreshes
        // whenever TranslationSource raises PropertyChanged("Item[]").
        return new Binding($"[{Key}]")
        {
            Source = TranslationSource.Instance,
            Mode = BindingMode.OneWay,
        };
    }
}
