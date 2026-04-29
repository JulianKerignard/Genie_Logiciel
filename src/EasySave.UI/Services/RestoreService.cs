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

        // Each top-level subdirectory timestamped as yyyyMMdd_HHmmss is treated as a
        // snapshot. If there is no such structure (flat target), expose the root itself.
        var snapshots = targetDir.GetDirectories("*", SearchOption.TopDirectoryOnly)
            .Where(d => DateTimeOffset.TryParseExact(d.Name, "yyyyMMdd_HHmmss",
                null, System.Globalization.DateTimeStyles.None, out _))
            .OrderByDescending(d => d.Name)
            .ToList();

        if (snapshots.Count > 0)
        {
            return snapshots.Select(d => BuildPoint(job, d, BackupType.Full)).ToList();
        }

        // Flat layout: single restore point from the target root.
        long size = targetDir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        return new[]
        {
            new RestorePoint
            {
                JobName = job.Name,
                Timestamp = targetDir.LastWriteTimeOffset(),
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

    private static RestorePoint BuildPoint(BackupJob job, DirectoryInfo dir, BackupType type)
    {
        long size = dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        DateTimeOffset.TryParseExact(dir.Name, "yyyyMMdd_HHmmss",
            null, System.Globalization.DateTimeStyles.None, out var ts);
        return new RestorePoint
        {
            JobName = job.Name,
            Timestamp = ts,
            BackupType = type,
            SizeBytes = size,
            SnapshotPath = dir.FullName,
        };
    }
}

// Extension so the restore service can turn a DirectoryInfo.LastWriteTime into DateTimeOffset.
file static class DirectoryInfoExtensions
{
    public static DateTimeOffset LastWriteTimeOffset(this DirectoryInfo dir) =>
        new DateTimeOffset(dir.LastWriteTimeUtc, TimeSpan.Zero);
}
