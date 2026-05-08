using EasyLog;
using EasySave.CLI;
using EasySave.Services;

AppConfig.Load();

var logger = new JsonDailyLogger(AppConfig.Instance.LogDirectory);
var backupManager = new BackupManager(
    logger,
    new FullBackupStrategy(),
    new DifferentialBackupStrategy(),
    StateTracker.Instance,
    JobRepository.Instance);
var langService = new LanguageService(AppConfig.Instance);

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (cliArgs.Length > 0)
{
    var parser = new CommandParser();
    var indices = parser.ParseJobSelection(cliArgs[0]);

    if (indices.Count == 0)
    {
        Console.Error.WriteLine(langService.T("error.invalid_selection"));
        // Non-zero exit code so wrapping shell scripts and CI pipelines
        // can detect the bad invocation; the previous `return;` left the
        // process exiting 0 alongside the stderr message.
        Environment.Exit(1);
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
