using EasySave;
using EasySave.Models;

namespace EasySave.UI.Services;

// TODO: replace with injected real service contract when dev2 merges RunJobAsync/PauseJob/ResumeJob
public interface IBackupManagerAdapter : IDisposable
{
    IReadOnlyList<BackupJob> GetJobs();
    void AddJob(BackupJob job);
    void RemoveJob(string name);
    Task RunJobAsync(string jobName, CancellationToken ct = default);
    void PauseJob(string jobName);
    void ResumeJob(string jobName);
    bool IsJobRunning(string jobName);
    event EventHandler<StateEntry>? StateUpdated;
}
