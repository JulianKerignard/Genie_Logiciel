using EasyLog;
using EasySave.CLI;
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
var backupManager = new BackupManager(
    logger,
    new FullBackupStrategy(),
    new DifferentialBackupStrategy(),
    StateTracker.Instance,
    JobRepository.Instance,
    encryption,
    AppConfig.Instance.Settings.EncryptedExtensions);
var langService = new LanguageService(AppConfig.Instance.Settings);

if (AppConfig.Instance.Settings.RemoteConsoleEnabled)
{
    IParallelBackupOrchestrator orchestrator = new NoOpParallelBackupOrchestrator();
    var remoteServer = new TcpRemoteConsoleServer(logger);

    remoteServer.CommandReceived += async cmd =>
    {
        logger.Append(new LogEntry
        {
            Timestamp = DateTimeOffset.Now.ToString("o"),
            JobName = cmd.JobName,
            SourceFile = $"[remote-cmd] {cmd.Action} from {cmd.SourceIp}",
            TargetFile = string.Empty,
            FileSize = 0,
            FileTransferTimeMs = 0,
        });

        switch (cmd.Action)
        {
            case CommandType.Pause: await orchestrator.PauseAsync(cmd.JobName); break;
            case CommandType.Play:  await orchestrator.ResumeAsync(cmd.JobName); break;
            case CommandType.Stop:  await orchestrator.StopAsync(cmd.JobName);  break;
        }
    };

    _ = remoteServer.StartAsync(AppConfig.Instance.Settings.RemoteConsolePort, CancellationToken.None)
        .ContinueWith(
            t => Console.Error.WriteLine($"[Remote] Server stopped unexpectedly: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
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
