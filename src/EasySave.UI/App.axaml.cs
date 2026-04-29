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

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        var langService = Services.GetRequiredService<ILanguageService>();
        TranslationSource.Instance.Initialize(langService);

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
            var cryptoSettings = AppConfig.Instance.Settings.CryptoSoft;
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
            AppConfig.Instance.Settings.EncryptedExtensions));

        // UI adapter layer
        services.AddSingleton<IBackupManagerAdapter, BackupManagerAdapter>();
        services.AddSingleton<BusinessWatcherService>();
        services.AddSingleton<IRestoreService>(_ => new RestoreService(JobRepository.Instance));
        services.AddSingleton<ISchedulerService, SchedulerService>();

        services.AddSingleton<MainWindowViewModel>();
    }

    private static void DisposeServices()
    {
        Services?.GetService<IBackupManagerAdapter>()?.Dispose();
        Services?.GetService<BusinessWatcherService>()?.Dispose();
    }
}
