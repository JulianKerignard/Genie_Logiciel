using EasySave;
using EasySave.Models;

namespace EasySave.UI.Services;

public interface IBackupManagerAdapter : IDisposable
{
    IReadOnlyList<BackupJob> GetJobs();
    void AddJob(BackupJob job);
    void RemoveJob(string name);

    /// <param name="startFromIndex">
    /// Files to skip at the start. 0 for a fresh run; non-zero when resuming a
    /// paused Full-backup job.
    /// </param>
    Task RunJobAsync(string jobName, int startFromIndex = 0, CancellationToken ct = default);

    /// <param name="reason">Human-readable pause reason written to state.json.</param>
    void PauseJob(string jobName, string reason = "UserRequested");

    void ResumeJob(string jobName);
    bool IsJobRunning(string jobName);
    event EventHandler<StateEntry>? StateUpdated;
}
