using EasyLog;
using EasySave.CLI;
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

var logger = new JsonDailyLogger(AppConfig.Instance.LogDirectory);
IEncryptionService encryption = string.IsNullOrWhiteSpace(AppConfig.Instance.Settings.CryptoSoft.Path)
    ? new NoOpEncryptionService()
    : new CryptoSoftAdapter(AppConfig.Instance.Settings.CryptoSoft);
// V3 priority extensions still apply in v1 console mode for the in-job
// order (priority files copy first within a single job). The cross-job
// gate is null because the v1 console runs jobs sequentially, never in
// parallel — no other job can hold the gate.
var backupManager = new BackupManager(
    logger,
    new FullBackupStrategy(),
    new DifferentialBackupStrategy(),
    StateTracker.Instance,
    JobRepository.Instance,
    encryption,
    AppConfig.Instance.Settings.EncryptedExtensions,
    bigFileGate: null,
    priorityGate: null,
    priorityExtensions: AppConfig.Instance.Settings.PriorityExtensions);
var langService = new LanguageService(AppConfig.Instance.Settings);

if (AppConfig.Instance.Settings.RemoteConsoleEnabled)
{
    var orchestrator = new ParallelBackupOrchestratorStub();
    var cert = AppConfig.Instance.Settings.RemoteConsoleTlsEnabled
        ? EasySave.Infrastructure.Remote.SelfSignedCertProvider.LoadOrCreate(
            EasySave.Infrastructure.Remote.SelfSignedCertProvider.DefaultCertPath())
        : null;
    var server = new TcpRemoteConsoleServer(logger, cert);
    server.CommandReceived += cmd =>
    {
        switch (cmd.Action)
        {
            case CommandType.Pause: orchestrator.Pause(cmd.JobName); break;
            case CommandType.Play: orchestrator.Resume(cmd.JobName); break;
            case CommandType.Stop: orchestrator.Stop(cmd.JobName); break;
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
    };
    using var serverCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; serverCts.Cancel(); };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => serverCts.Cancel();
    _ = server.StartAsync(AppConfig.Instance.Settings.RemoteConsolePort, serverCts.Token);
}

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
