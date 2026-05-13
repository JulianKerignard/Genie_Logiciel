using EasySave;
using EasySave.Models;

namespace EasySave.UI.Services;

public interface IBackupManagerAdapter : IDisposable
{
    IReadOnlyList<BackupJob> GetJobs();
    void AddJob(BackupJob job);
    void RemoveJob(string name);

    /// <param name="resumeAfterPath">
    /// Resume a paused Full-backup job at the file ordinal-strictly-after this
    /// path. Null (default) means a fresh run from the start. Tracking by path
    /// (instead of by index) is robust to source mutations between pause and
    /// resume — files added or removed no longer offset the cursor.
    /// </param>
    Task RunJobAsync(string jobName, string? resumeAfterPath = null, CancellationToken ct = default);

    /// <param name="reason">Human-readable pause reason written to state.json.</param>
    void PauseJob(string jobName, string reason = "UserRequested");

    void ResumeJob(string jobName);
    bool IsJobRunning(string jobName);
    event EventHandler<StateEntry>? StateUpdated;
}
