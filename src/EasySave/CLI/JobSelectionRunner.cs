using EasySave.Models;
using EasySave.Services;

namespace EasySave.CLI;

internal static class JobSelectionRunner
{
    // indices are 1-based; writeError and writeInfo let callers route to stdout vs stderr
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
