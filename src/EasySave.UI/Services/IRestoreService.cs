using EasySave.Models;
using EasySave.UI.Models;

namespace EasySave.UI.Services;

/// <summary>
/// Provides restore-point discovery and file restoration for backup jobs.
/// </summary>
public interface IRestoreService
{
    /// <summary>
    /// Returns the list of available restore points for the given job,
    /// ordered from most recent to oldest.
    /// </summary>
    IReadOnlyList<RestorePoint> GetRestorePoints(BackupJob job);

    /// <summary>
    /// Restores a snapshot to <paramref name="destination"/> (or the job's
    /// original source when null). Reports progress via <paramref name="onProgress"/>
    /// (0–100) on the caller's thread. Returns the resolved destination path
    /// so callers can surface it in the UI — operators kept reporting the
    /// restore as "did nothing" when the fallback was silent.
    /// </summary>
    Task<string> RestoreAsync(
        RestorePoint point,
        string? destination,
        IProgress<int>? onProgress,
        CancellationToken ct = default);
}
