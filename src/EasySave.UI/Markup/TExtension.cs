using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace EasySave.UI.Markup;

/// <summary>
/// XAML markup extension that provides reactive translated strings.
/// Returns a <see cref="DynamicResourceExtension"/> so the UI refreshes
/// automatically when <see cref="EasySave.UI.Services.LanguageService"/>
/// updates <c>Application.Current.Resources</c> on locale change.
/// </summary>
/// <remarks>
/// We previously bound through an indexer on a <c>TranslationSource</c>
/// singleton, but Avalonia's binding engine did not consistently refresh
/// indexer paths (notably inside button content templated through a
/// custom <c>ControlTheme</c>) when <see cref="System.ComponentModel.PropertyChangedEventArgs"/>
/// was raised with the WPF "Item[]" sentinel. <see cref="DynamicResourceExtension"/>
/// is the documented Avalonia mechanism for runtime resource swap and is
/// guaranteed to re-evaluate every consumer.
/// </remarks>
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
        return new DynamicResourceExtension(Key).ProvideValue(serviceProvider);
    }
}
