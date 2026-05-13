using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyLog;
using EasySave.Infrastructure.Events;
using EasySave.Infrastructure.Remote;
using EasySave.Services;
using EasySave.Shared;
using EasySave.UI.Services;
using EasySave.UI.ViewModels;
using EasySave.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EasySave.UI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    // Cancellation source for the in-process TCP server. Cancelled in
    // DisposeServices so the listener stops accepting and exits its loop
    // before the process exits.
    private static CancellationTokenSource? _remoteServerCts;

    // Kept alive so DisposeServices can drain the buffered HTTP queue
    // before the window closes — otherwise an in-flight POST gets dropped
    // when the singleton IDailyLogger is collected.
    private static IAsyncDisposable? _logShipper;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load appsettings.json before any service reads AppConfig.Instance.
        try
        {
            AppConfig.Load();
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.TraceError(
                $"[App] appsettings.json read failed ({ex.Message}). Aborting startup.");
            Environment.Exit(1);
        }

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

        // Apply the language the user picked on a previous run so the app
        // opens in their chosen locale, not the LanguageService default ("fr").
        var savedLocale = SettingsRepository.Instance.Load().Language;
        if (!string.IsNullOrWhiteSpace(savedLocale))
        {
            langService.SetLanguage(savedLocale);
        }

        Services.GetRequiredService<SchedulerDispatchService>().Start();

        // V3 business-software auto-pause path. Hook the bridge into the
        // watcher BEFORE JobsViewModel.ctor calls watcher.Start(), so the
        // very first appear/gone tick is observed and PauseAll/ResumeAll
        // get fired on the orchestrator. No-op when no v3 jobs are
        // running (PauseAll iterates an empty live job map).
        Services.GetRequiredService<EasySave.Services.BusinessSoftwareControllerBridge>().Start();

        // V3 remote console wiring. Gated on appsettings.json so the GUI
        // boot stays minimal for operators who don't need a remote operator
        // station. When enabled:
        //   - StateTrackerEventBridge publishes JobProgress events on the bus,
        //   - RemoteConsoleBroadcastBridge forwards them to every connected
        //     client over TCP,
        //   - the server's CommandReceived event routes Pause/Play/Stop into
        //     the BackupManagerAdapter.
        if (AppConfig.Instance.Settings.RemoteConsoleEnabled)
        {
            StartRemoteConsole(Services);
        }

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
            var settings = SettingsRepository.Instance.Load();
            var (logger, shipper) = DailyLoggerFactory.Create(
                AppConfig.Instance.LogDirectory,
                settings.LogFormat,
                settings.LogMode,
                settings.LogCentralizedEndpoint);
            _logShipper = shipper;
            return logger;
        });

        services.AddSingleton<IEncryptionService>(_ =>
        {
            var cryptoSettings = userSettings.CryptoSoft;
            return string.IsNullOrWhiteSpace(cryptoSettings.Path)
                ? new NoOpEncryptionService()
                : (IEncryptionService)new CryptoSoftAdapter(cryptoSettings);
        });

        // V3 big-file gate. SemaphoreSlim(N=1) shared across the engine —
        // when parallel jobs each try to copy a file >= threshold, the gate
        // serializes them so disk/network bandwidth is not saturated.
        services.AddSingleton<IBigFileGate>(_ =>
            new BigFileGate(userSettings.LargeFileThresholdKb * 1024L));

        // V3 priority gate. Cross-job barrier enforcing the CdC rule
        // « Aucune sauvegarde d'un fichier non prioritaire ne peut se
        //   faire tant qu'il y a des extensions prioritaires en attente
        //   sur au moins un travail. »
        // Shared by every job; non-priority files of any job park here
        // until every job has finished its priority files.
        services.AddSingleton<IPriorityGate>(_ => new PriorityGate());

        services.AddSingleton<BackupManager>(sp => new BackupManager(
            sp.GetRequiredService<IDailyLogger>(),
            new FullBackupStrategy(),
            new DifferentialBackupStrategy(),
            StateTracker.Instance,
            JobRepository.Instance,
            sp.GetRequiredService<IEncryptionService>(),
            userSettings.EncryptedExtensions,
            sp.GetRequiredService<IBigFileGate>(),
            sp.GetRequiredService<IPriorityGate>(),
            userSettings.PriorityExtensions));

        // V3 parallel orchestrator + BackupManager-backed job runner.
        // The orchestrator wraps RunAllAsync (see JobsViewModel) so multiple
        // jobs run concurrently bounded by max_parallel_jobs.
        services.AddSingleton<IJobRunner>(sp =>
            new BackupManagerJobRunner(sp.GetRequiredService<BackupManager>()));
        services.AddSingleton<IParallelBackupOrchestrator>(sp =>
            new ParallelBackupOrchestrator(
                sp.GetRequiredService<IJobRunner>(),
                _ => sp.GetRequiredService<IDailyLogger>(),
                Math.Max(1, userSettings.MaxParallelJobs)));

        // UI adapter layer
        services.AddSingleton<IBackupManagerAdapter, BackupManagerAdapter>();
        services.AddSingleton<BusinessWatcherService>();
        // Engine-level bridge that hooks the v3 IJobController surface to
        // the watcher: when a configured process appears, PauseAll() is
        // called on every running job, and ResumeAll() runs on the
        // trailing edge. Subscribes via IBusinessSoftwareSignals so the
        // bridge stays UI-agnostic (testable with a stub).
        services.AddSingleton<EasySave.Services.BusinessSoftwareControllerBridge>(sp =>
            new EasySave.Services.BusinessSoftwareControllerBridge(
                sp.GetRequiredService<BusinessWatcherService>(),
                sp.GetRequiredService<IParallelBackupOrchestrator>(),
                sp.GetRequiredService<IDailyLogger>()));
        services.AddSingleton<IRestoreService>(_ => new RestoreService(JobRepository.Instance));
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<SchedulerDispatchService>();

        // V3 event-bus + bridges. Registered unconditionally (cheap to
        // construct, no thread or socket allocated until Start is called);
        // the actual server + bridge startup is gated by
        // RemoteConsoleEnabled in OnFrameworkInitializationCompleted.
        services.AddSingleton<IEventBus>(_ => new ChannelEventBus());
        services.AddSingleton<IRemoteConsoleServer>(sp =>
        {
            // Load the self-signed cert once at registration time when TLS
            // is enabled — generates the file on first run, then reuses the
            // same key pair across restarts so clients' TOFU pins stay valid.
            var cert = AppConfig.Instance.Settings.RemoteConsoleTlsEnabled
                ? EasySave.Infrastructure.Remote.SelfSignedCertProvider.LoadOrCreate(
                    EasySave.Infrastructure.Remote.SelfSignedCertProvider.DefaultCertPath())
                : null;
            return new TcpRemoteConsoleServer(sp.GetRequiredService<IDailyLogger>(), cert);
        });
        services.AddSingleton(sp =>
            new StateTrackerEventBridge(StateTracker.Instance, sp.GetRequiredService<IEventBus>()));
        services.AddSingleton(sp =>
            new RemoteConsoleBroadcastBridge(
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<IRemoteConsoleServer>()));

        services.AddSingleton<MainWindowViewModel>();
    }

    private static void StartRemoteConsole(IServiceProvider services)
    {
        var server = services.GetRequiredService<IRemoteConsoleServer>();
        var backup = services.GetRequiredService<IBackupManagerAdapter>();
        var orchestrator = services.GetRequiredService<IParallelBackupOrchestrator>();
        var logger = services.GetRequiredService<IDailyLogger>();

        server.CommandReceived += cmd => HandleRemoteCommandAsync(cmd, backup, orchestrator, logger);

        services.GetRequiredService<StateTrackerEventBridge>().Start();
        services.GetRequiredService<RemoteConsoleBroadcastBridge>().Start();

        _remoteServerCts = new CancellationTokenSource();
        // Fire-and-forget: the listener loop runs until the CTS is cancelled
        // in DisposeServices. Surface unexpected exits to Trace so they don't
        // disappear silently.
        _ = server.StartAsync(AppConfig.Instance.Settings.RemoteConsolePort, _remoteServerCts.Token)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    System.Diagnostics.Trace.TraceError(
                        $"[App] Remote console server stopped with error: {t.Exception?.GetBaseException().Message}");
                }
            }, TaskScheduler.Default);
    }

    private static Task HandleRemoteCommandAsync(CommandDto cmd, IBackupManagerAdapter backup, IParallelBackupOrchestrator orchestrator, IDailyLogger logger)
    {
        // Route the command into the same adapter the GUI uses, so a remote
        // operator sees identical job behaviour to a local one.
        try
        {
            switch (cmd.Action)
            {
                case CommandType.Pause:
                    backup.PauseJob(cmd.JobName, "RemoteCommand");
                    break;
                case CommandType.Play:
                    // ResumeJob is a no-op if the job is not paused; the
                    // follow-up RunJobAsync is a no-op if the job is already
                    // running. Together they cover both "resume" and "start
                    // from idle" without the caller having to inspect state.
                    backup.ResumeJob(cmd.JobName);
                    // Observe the task so async failures (job not found,
                    // orchestrator throw) surface to Trace — same posture as
                    // StartAsync above. Without this continuation an async
                    // fault disappears silently with no audit signal.
                    _ = backup.RunJobAsync(cmd.JobName)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                System.Diagnostics.Trace.TraceError(
                                    $"[App] RunJobAsync('{cmd.JobName}') from remote Play failed: {t.Exception?.GetBaseException().Message}");
                            }
                        }, TaskScheduler.Default);
                    break;
                case CommandType.Stop:
                    // Cancel the per-job CTS via the orchestrator. The worker
                    // halts at the next file boundary and the job transitions
                    // to Inactive (not Paused) — matching the CdC v3 contract
                    // « Stop = arrêt immédiat du travail et de la tache en
                    // cours ». A subsequent Play starts a fresh run from the
                    // first file, not a resume from the saved offset.
                    orchestrator.Stop(cmd.JobName);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Never let a command handler fail back to the TCP server's
            // dispatch loop — the server isolates per-handler exceptions,
            // but losing the audit log line would be worse than the throw.
            System.Diagnostics.Trace.TraceError(
                $"[App] Remote command {cmd.Action} on '{cmd.JobName}' failed: {ex.Message}");
        }

        logger.Append(new LogEntry
        {
            Timestamp = DateTimeOffset.Now.ToString("o"),
            JobName = cmd.JobName,
            SourceFile = cmd.SourceIp ?? string.Empty,
            TargetFile = string.Empty,
            FileSize = 0,
            FileTransferTimeMs = 0,
            EventType = LogEvent.CommandReceived,
        });

        return Task.CompletedTask;
    }

    private static void DisposeServices()
    {
        // Stop the TCP listener first so no new commands land while we are
        // tearing down the adapter / bridges they would forward to.
        _remoteServerCts?.Cancel();
        try { Services?.GetService<IRemoteConsoleServer>()?.StopAsync().GetAwaiter().GetResult(); }
        catch { /* shutdown best-effort */ }
        _remoteServerCts?.Dispose();
        _remoteServerCts = null;

        // RemoteConsoleBroadcastBridge has no Stop/Dispose — the bus it
        // subscribed to is disposed below, which drops the consumer task
        // and releases the subscription transitively.
        Services?.GetService<StateTrackerEventBridge>()?.Dispose();
        (Services?.GetService<IEventBus>() as IDisposable)?.Dispose();

        Services?.GetService<SchedulerDispatchService>()?.Dispose();
        Services?.GetService<IBackupManagerAdapter>()?.Dispose();
        Services?.GetService<EasySave.Services.BusinessSoftwareControllerBridge>()?.Dispose();
        Services?.GetService<BusinessWatcherService>()?.Dispose();

        // V3 orchestrator + gate. Dispose order: orchestrator first so any
        // in-flight slot wait surfaces as ObjectDisposedException (handled
        // inside the orchestrator as Cancelled), then the gate which the
        // orchestrator's job runners may still hold transiently.
        Services?.GetService<IParallelBackupOrchestrator>()?.Dispose();
        (Services?.GetService<IBigFileGate>() as IDisposable)?.Dispose();
        (Services?.GetService<IPriorityGate>() as IDisposable)?.Dispose();

        // Drain the centralized log shipper last so any entry the engine
        // produced during teardown (e.g. "job stopped") has a chance to
        // be POSTed before the queue is abandoned.
        try { _logShipper?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { /* shutdown best-effort */ }
        _logShipper = null;
    }
}
