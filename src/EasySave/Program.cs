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
        return;
    }

    var jobs = backupManager.ListJobs();
    foreach (var idx in indices)
    {
        if (idx < 1 || idx > jobs.Count)
        {
            Console.Error.WriteLine(string.Format(langService.T("error.job_not_found"), idx));
            continue;
        }

        Console.WriteLine(string.Format(langService.T("job.executing"), jobs[idx - 1].Name));
        try
        {
            backupManager.ExecuteJob(jobs[idx - 1].Name);
            Console.WriteLine(string.Format(langService.T("job.done"), jobs[idx - 1].Name));
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine(langService.T("error.source_not_found"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Format(langService.T("error.execute_failed"), ex.Message));
        }
    }
}
else
{
    var ui = new ConsoleUI(backupManager, langService);
    ui.Run();
}
