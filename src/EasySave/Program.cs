using EasyLog;
using EasySave.CLI;
using EasySave.Infrastructure.Events;
using EasySave.Infrastructure.Remote;
using EasySave.Services;
using EasySave.Shared;

try
{
    AppConfig.Load();
}
catch (IOException ex)
{
    Console.Error.WriteLine($"[Fatal] Cannot read appsettings.json: {ex.Message}");
    Environment.Exit(1);
}

var (logger, logShipper) = DailyLoggerFactory.Create(
    AppConfig.Instance.LogDirectory,
    AppConfig.Instance.Settings.LogFormat,
    AppConfig.Instance.Settings.LogMode,
    AppConfig.Instance.Settings.LogCentralizedEndpoint);

IEncryptionService encryption = string.IsNullOrWhiteSpace(AppConfig.Instance.Settings.CryptoSoft.Path)
    ? new NoOpEncryptionService()
    : new CryptoSoftAdapter(AppConfig.Instance.Settings.CryptoSoft);

// V3 cross-job gates. In console mode the local ConsoleUI runs jobs
// sequentially via BackupManager.ExecuteJob (no parallelism), but when
// RemoteConsoleEnabled = true the remote client can launch jobs through
// the parallel orchestrator below — those need the gates wired so big
// files serialize and priority extensions get the cross-job barrier.
// Mirrored from App.axaml.cs DI registration with the same [64 KB, 10 GB]
// clamp so a hand-edited appsettings.json cannot crash the BigFileGate ctor.
const int MinThresholdKb = 64;
const int MaxThresholdKb = 10 * 1024 * 1024; // 10 GB
int clampedKb = Math.Clamp(AppConfig.Instance.Settings.LargeFileThresholdKb, MinThresholdKb, MaxThresholdKb);
IBigFileGate bigFileGate = new BigFileGate(clampedKb * 1024L);
IPriorityGate priorityGate = new PriorityGate();

var backupManager = new BackupManager(
    logger,
    new FullBackupStrategy(),
    new DifferentialBackupStrategy(),
    StateTracker.Instance,
    JobRepository.Instance,
    encryption,
    AppConfig.Instance.Settings.EncryptedExtensions,
    bigFileGate,
    priorityGate,
    AppConfig.Instance.Settings.PriorityExtensions);
var langService = new LanguageService(AppConfig.Instance.Settings);

// V3 remote-console wiring. Conditional disposables hoisted to top scope
// so the consolidated ProcessExit handler below can tear them down in the
// correct order (server first, then bridges/bus, then orchestrator, then
// gates, then logger/shipper last).
CancellationTokenSource? serverCts = null;
IRemoteConsoleServer? server = null;
StateTrackerEventBridge? stateBridge = null;
IEventBus? eventBus = null;
IParallelBackupOrchestrator? orchestrator = null;

if (AppConfig.Instance.Settings.RemoteConsoleEnabled)
{
    // V3 real parallel orchestrator + EventBus chain. Mirrors the GUI
    // wiring in App.axaml.cs:170-235 so a remote client connected to
    // the console binary sees identical pause/resume/stop semantics
    // and receives JobProgressDto snapshots in real time. Previously
    // this branch instantiated ParallelBackupOrchestratorStub, whose
    // Pause/Resume/Stop were no-ops — every remote command silently
    // dropped, and clients received zero progress events because no
    // bridge published StateTracker updates onto the bus.
    IJobRunner runner = new BackupManagerJobRunner(backupManager);
    orchestrator = new ParallelBackupOrchestrator(
        runner,
        _ => logger,
        Math.Max(1, AppConfig.Instance.Settings.MaxParallelJobs));

    var cert = AppConfig.Instance.Settings.RemoteConsoleTlsEnabled
        ? SelfSignedCertProvider.LoadOrCreate(SelfSignedCertProvider.DefaultCertPath())
        : null;
    server = new TcpRemoteConsoleServer(logger, cert);

    eventBus = new ChannelEventBus();
    stateBridge = new StateTrackerEventBridge(StateTracker.Instance, eventBus);
    var broadcastBridge = new RemoteConsoleBroadcastBridge(eventBus, server);
    stateBridge.Start();
    broadcastBridge.Start();

    server.CommandReceived += cmd => HandleRemoteCommandAsync(cmd, orchestrator, logger);

    // Plain `var`: a `using var` here would scope-dispose the CTS at the
    // end of the if-block, and the consolidated ProcessExit handler below
    // would then call Cancel/Dispose on a disposed CTS. Lifetime is owned
    // by the ProcessExit handler instead.
    serverCts = new CancellationTokenSource();
    var ctsForCancel = serverCts;
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctsForCancel.Cancel(); };

    _ = server.StartAsync(AppConfig.Instance.Settings.RemoteConsolePort, serverCts.Token)
        .ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                System.Diagnostics.Trace.TraceError(
                    $"[Program] Remote console server stopped with error: {t.Exception?.GetBaseException().Message}");
            }
        }, TaskScheduler.Default);

    static Task HandleRemoteCommandAsync(
        CommandDto cmd,
        IParallelBackupOrchestrator orchestrator,
        IDailyLogger logger)
    {
        try
        {
            switch (cmd.Action)
            {
                case CommandType.Pause:
                    // Resets the job's PauseGate; the worker thread parks at
                    // the next file boundary and resumes from the same offset
                    // when Play arrives.
                    orchestrator.Pause(cmd.JobName);
                    break;
                case CommandType.Play:
                    // Two-path Play (matches App.axaml.cs:268-281):
                    //  - If the job is currently parked (Pause was sent earlier),
                    //    Resume() signals its PauseGate and the worker continues
                    //    from the same offset.
                    //  - If the job is not running, RunAsync starts a fresh run
                    //    so the job becomes orchestrator-tracked and is then
                    //    controllable by Pause/Stop.
                    // When the job is already running (paused or active), the
                    // RunAsync call returns a Failed JobResult ("already running")
                    // which we silently discard — not a fault, no Trace warning.
                    // The continuation only logs genuine async exceptions.
                    orchestrator.Resume(cmd.JobName);
                    _ = orchestrator.RunAsync(new[] { cmd.JobName }, CancellationToken.None)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                System.Diagnostics.Trace.TraceError(
                                    $"[Program] RunAsync('{cmd.JobName}') from remote Play failed: {t.Exception?.GetBaseException().Message}");
                            }
                        }, TaskScheduler.Default);
                    break;
                case CommandType.Stop:
                    // Cancels the per-job CTS via the orchestrator; the
                    // worker halts at the next file boundary and the job
                    // ends Inactive (CdC v3: « Stop = arrêt immédiat »).
                    orchestrator.Stop(cmd.JobName);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(
                $"[Program] Remote command {cmd.Action} on '{cmd.JobName}' failed: {ex.Message}");
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
}

// Single ProcessExit handler covering BOTH paths (with / without
// RemoteConsole) in correct teardown order. ProcessExit fires handlers in
// FIFO registration order: registering the logger separately upstream
// would dispose it first while the server/bridges were still alive, and
// any in-flight CommandReceived audit Append would be silently dropped
// (post-Dispose Append is a documented no-op in JsonDailyLogger).
//
// Order matches App.axaml.cs:DisposeServices: stop accepting new work,
// drain consumers, dispose engine primitives, then logger + shipper last
// so any "shutting down" entry the engine produces still reaches disk.
//
// Sync handler — async lambdas would be async void and the runtime would
// not wait for the channel drain, losing buffered entries on shutdown.
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    serverCts?.Cancel();
    try { server?.StopAsync().GetAwaiter().GetResult(); } catch { /* best-effort shutdown */ }
    stateBridge?.Dispose();
    (eventBus as IDisposable)?.Dispose();
    orchestrator?.Dispose();
    (bigFileGate as IDisposable)?.Dispose();
    (priorityGate as IDisposable)?.Dispose();
    // Logger first, then shipper: the logger's writer loop forwards entries
    // to the shipper, so the logger must finish draining BEFORE the shipper
    // is torn down — otherwise a late forward hits a disposed HttpClient and
    // the entry is lost in centralized mode.
    (logger as IDisposable)?.Dispose();
    logShipper?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    serverCts?.Dispose();
};

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (cliArgs.Length > 0)
{
    var parser = new CommandParser();
    var indices = parser.ParseJobSelection(cliArgs[0]);

    if (indices.Count == 0)
    {
        Console.Error.WriteLine(langService.T("error.invalid_selection"));
        return;
    }

    JobSelectionRunner.Execute(
        indices,
        backupManager.ListJobs(),
        backupManager,
        langService,
        Console.WriteLine,
        Console.Error.WriteLine);
}
else
{
    var ui = new ConsoleUI(backupManager, langService);
    ui.Run();
}
