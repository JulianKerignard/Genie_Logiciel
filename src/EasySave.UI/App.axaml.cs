using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasySave.UI.Services;
using EasySave.UI.ViewModels;
using EasySave.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EasySave.UI;

/// <summary>
/// Application entry point. Configures the DI container, wires the
/// <see cref="TranslationSource"/> singleton, and opens the main window.
/// </summary>
public partial class App : Application
{
    /// <summary>The application-wide DI service provider, available after startup.</summary>
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Wire the reactive translation bridge used by {markup:T} extensions.
        var langService = Services.GetRequiredService<ILanguageService>();
        TranslationSource.Instance.Initialize(langService);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILanguageService, LanguageService>();
        services.AddTransient<MainWindowViewModel>();
    }
}
