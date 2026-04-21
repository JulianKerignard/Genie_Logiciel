using EasySave.Models;
using EasySave.Services;

namespace EasySave.CLI;

/// <summary>
/// Runs a pre-parsed list of job indices against the backup manager and reports
/// each result through the language service. Shared by the interactive menu
/// (<see cref="ConsoleUI"/>) and the direct-mode CLI (<c>Program.Main</c>) so
/// the execution feedback stays consistent and i18n-correct everywhere.
/// </summary>
internal static class JobSelectionRunner
{
    /// <summary>
    /// Executes each <paramref name="indices"/> entry against the matching job in
    /// <paramref name="jobs"/>. Invalid indices and job failures are reported via
    /// <paramref name="writeError"/>; successes via <paramref name="writeInfo"/>.
    /// Continues on failure so a single bad job does not abort the batch.
    /// </summary>
    public static void Execute(
        IReadOnlyList<int> indices,
        IReadOnlyList<BackupJob> jobs,
        BackupManager backupManager,
        LanguageService lang,
        Action<string> writeInfo,
        Action<string> writeError)
    {
        foreach (var idx in indices)
        {
            if (idx < 1 || idx > jobs.Count)
            {
                writeError(string.Format(lang.T("error.job_not_found"), idx));
                continue;
            }

            var name = jobs[idx - 1].Name;
            writeInfo(string.Format(lang.T("job.executing"), name));
            try
            {
                backupManager.ExecuteJob(name);
                writeInfo(string.Format(lang.T("job.done"), name));
            }
            catch (DirectoryNotFoundException)
            {
                writeError(lang.T("error.source_not_found"));
            }
            catch (Exception ex)
            {
                writeError(string.Format(lang.T("error.execute_failed"), ex.Message));
            }
        }
    }
}
