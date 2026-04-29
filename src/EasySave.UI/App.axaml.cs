using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyLog;
using EasySave.Services;
using EasySave.UI.Services;
using EasySave.UI.ViewModels;
using EasySave.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EasySave.UI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load appsettings.json before any service reads AppConfig.Instance.
        AppConfig.Load();

        // Seed settings.json from appsettings.json defaults on first run so the
        // GUI Settings screen reflects the bootstrap configuration. After the
        // seed, settings.json is the source of truth for all user-managed
        // values (encrypted extensions, business software, CryptoSoft path).
        if (!File.Exists(AppConfig.Instance.SettingsFilePath))
        {
            SettingsRepository.Instance.Save(AppConfig.Instance.Settings);
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        var langService = Services.GetRequiredService<ILanguageService>();
        TranslationSource.Instance.Initialize(langService);

        Services.GetRequiredService<SchedulerDispatchService>().Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
            mainWindow.Closing += (_, _) => DisposeServices();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILanguageService, EasySave.UI.Services.LanguageService>();

        // Read user-managed settings once at startup. GUI edits saved while the
        // app is running take effect after the next launch — BackupManager is a
        // singleton with a frozen list of encrypted extensions.
        var userSettings = SettingsRepository.Instance.Load();

        // Backend services
        services.AddSingleton<IDailyLogger>(_ =>
        {
            var logFormat = SettingsRepository.Instance.Load().LogFormat;
            return logFormat.Equals("xml", StringComparison.OrdinalIgnoreCase)
                ? (IDailyLogger)new XmlDailyLogger(AppConfig.Instance.LogDirectory)
                : new JsonDailyLogger(AppConfig.Instance.LogDirectory);
        });

        services.AddSingleton<IEncryptionService>(_ =>
        {
            var cryptoSettings = userSettings.CryptoSoft;
            return string.IsNullOrWhiteSpace(cryptoSettings.Path)
                ? new NoOpEncryptionService()
                : (IEncryptionService)new CryptoSoftAdapter(cryptoSettings);
        });

        services.AddSingleton<BackupManager>(sp => new BackupManager(
            sp.GetRequiredService<IDailyLogger>(),
            new FullBackupStrategy(),
            new DifferentialBackupStrategy(),
            StateTracker.Instance,
            JobRepository.Instance,
            sp.GetRequiredService<IEncryptionService>(),
            userSettings.EncryptedExtensions));

        // UI adapter layer
        services.AddSingleton<IBackupManagerAdapter, BackupManagerAdapter>();
        services.AddSingleton<BusinessWatcherService>();
        services.AddSingleton<IRestoreService>(_ => new RestoreService(JobRepository.Instance));
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<SchedulerDispatchService>();

        services.AddSingleton<MainWindowViewModel>();
    }

    private static void DisposeServices()
    {
        Services?.GetService<SchedulerDispatchService>()?.Dispose();
        Services?.GetService<IBackupManagerAdapter>()?.Dispose();
        Services?.GetService<BusinessWatcherService>()?.Dispose();
    }
}
