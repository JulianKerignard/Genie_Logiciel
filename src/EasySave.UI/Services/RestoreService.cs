using EasySave.Models;
using EasySave.Services;
using EasySave.UI.Models;

namespace EasySave.UI.Services;

/// <summary>
/// Discovers restore points by scanning each job's target directory and
/// restores files by copying them back to the source (or an alternative path).
/// </summary>
public sealed class RestoreService : IRestoreService
{
    private readonly JobRepository _jobs;

    public RestoreService(JobRepository jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        _jobs = jobs;
    }

    /// <inheritdoc />
    public IReadOnlyList<RestorePoint> GetRestorePoints(BackupJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var targetDir = new DirectoryInfo(job.TargetPath);
        if (!targetDir.Exists) return Array.Empty<RestorePoint>();

        // BackupManager writes files flat into TargetPath (no versioned subdirectories),
        // so the target root is the only restore point available.
        long size = targetDir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        return new[]
        {
            new RestorePoint
            {
                JobName = job.Name,
                Timestamp = new DateTimeOffset(targetDir.LastWriteTimeUtc, TimeSpan.Zero),
                BackupType = job.Type,
                SizeBytes = size,
                SnapshotPath = targetDir.FullName,
            },
        };
    }

    /// <inheritdoc />
    public async Task RestoreAsync(
        RestorePoint point,
        string? destination,
        IProgress<int>? onProgress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(point);

        var source = new DirectoryInfo(point.SnapshotPath);
        if (!source.Exists) throw new DirectoryNotFoundException(point.SnapshotPath);

        var destPath = string.IsNullOrWhiteSpace(destination)
            ? ResolveOriginalSource(point.JobName)
            : destination;

        Directory.CreateDirectory(destPath);

        var files = source.GetFiles("*", SearchOption.AllDirectories);
        int total = files.Length;
        int done = 0;

        await Task.Run(() =>
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source.FullName, file.FullName);
                var target = Path.Combine(destPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file.FullName, target, overwrite: true);
                done++;
                onProgress?.Report(total == 0 ? 100 : done * 100 / total);
            }
        }, ct).ConfigureAwait(false);
    }

    private string ResolveOriginalSource(string jobName)
    {
        var job = _jobs.Load()
            .FirstOrDefault(j => j.Name.Equals(jobName, StringComparison.OrdinalIgnoreCase));
        return job?.SourcePath ?? Directory.GetCurrentDirectory();
    }
}
