using EasyLog;
using EasySave.CLI;
using EasySave.Services;

AppConfig.Load();

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
