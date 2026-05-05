using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using EasySave.RemoteConsole.Infrastructure;
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
        LoadLocale("en");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var client = new TcpRemoteConsoleClient();
            var vm = new ConsoleViewModel(client);
            desktop.MainWindow = new MainWindow { DataContext = vm };

            desktop.Exit += (_, _) =>
            {
                vm.Dispose();
                client.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Loads translation keys from the embedded asset and pushes them into
    // Application.Resources so {DynamicResource key} (via {rc:T Key=...}) resolves.
    private void LoadLocale(string locale)
    {
        var uri = new Uri($"avares://EasySave.RemoteConsole/Assets/i18n/{locale}.json");
        try
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
                               ?? new Dictionary<string, string>();
            foreach (var (key, value) in translations)
                Resources[key] = value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"[App] Failed to load locale '{locale}': {ex.Message}");
        }
    }
}
