using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace EasySave.RemoteConsole.Markup;

/// <summary>
/// XAML markup extension that resolves a translation key via Application.Current.Resources.
/// Uses DynamicResourceExtension so every consumer refreshes on locale change.
/// </summary>
/// <example><code>&lt;TextBlock Text="{rc:T Key=console.connect}" /&gt;</code></example>
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TExtension() { }
    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new DynamicResourceExtension(Key).ProvideValue(serviceProvider);
}
