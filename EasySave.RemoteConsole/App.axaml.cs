using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasySave.RemoteConsole.Infrastructure;
using EasySave.RemoteConsole.Services;
using EasySave.RemoteConsole.ViewModels;
using EasySave.RemoteConsole.Views;

namespace EasySave.RemoteConsole;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var client = new TcpRemoteConsoleClient();
            var vm = new ConsoleViewModel(client, new LanguageService(lang));
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.Exit += (_, _) => vm.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
