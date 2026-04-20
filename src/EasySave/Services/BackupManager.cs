using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Manages backup jobs: CRUD + execution with logging and state tracking.
public class BackupManager
{
    private readonly IDailyLogger _logger;
    private readonly IBackupStrategy _fullStrategy;
    private readonly IBackupStrategy _diffStrategy;
    private readonly StateTracker _stateTracker;
    private readonly JobRepository _jobRepository;

    public BackupManager(
        IDailyLogger logger,
        IBackupStrategy fullStrategy,
        IBackupStrategy diffStrategy,
        StateTracker stateTracker,
        JobRepository jobRepository)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fullStrategy);
        ArgumentNullException.ThrowIfNull(diffStrategy);
        ArgumentNullException.ThrowIfNull(stateTracker);
        ArgumentNullException.ThrowIfNull(jobRepository);

        _logger = logger;
        _fullStrategy = fullStrategy;
        _diffStrategy = diffStrategy;
        _stateTracker = stateTracker;
        _jobRepository = jobRepository;
    }

    public void AddJob(BackupJob job) => throw new NotImplementedException();

    public void RemoveJob(string name) => throw new NotImplementedException();

    public IReadOnlyList<BackupJob> ListJobs() => throw new NotImplementedException();

    public void ExecuteJob(string name)
    {
        var jobs = _jobRepository.Load();
        var job = jobs.FirstOrDefault(j => j.Name == name)
            ?? throw new InvalidOperationException($"Job '{name}' not found.");

        var strategy = job.Type == BackupType.Full ? _fullStrategy : _diffStrategy;
        var sourceDir = new DirectoryInfo(job.SourcePath);

        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {job.SourcePath}");

        var allFiles = sourceDir.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        var filesToCopy = allFiles
            .Where(f => strategy.ShouldCopy(f, BuildTargetPath(f, sourceDir, job.TargetPath)))
            .ToList();

        int totalFiles = filesToCopy.Count;
        long totalSize = filesToCopy.Sum(f => f.Length);

        _stateTracker.Update(new StateEntry
        {
            Name = job.Name,
            LastActionTime = DateTimeOffset.Now,
            State = JobState.Active,
            TotalFilesEligible = totalFiles,
            TotalSize = totalSize,
            FilesRemaining = totalFiles,
            SizeRemaining = totalSize,
        });

        int filesProcessed = 0;
        long sizeProcessed = 0;

        foreach (var file in filesToCopy)
        {
            string targetPath = BuildTargetPath(file, sourceDir, job.TargetPath);

            _stateTracker.Update(new StateEntry
            {
                Name = job.Name,
                LastActionTime = DateTimeOffset.Now,
                State = JobState.Active,
                TotalFilesEligible = totalFiles,
                TotalSize = totalSize,
                FilesRemaining = totalFiles - filesProcessed,
                SizeRemaining = totalSize - sizeProcessed,
                CurrentSource = file.FullName,
                CurrentTarget = targetPath,
            });

            long transferTimeMs;
            var sw = Stopwatch.StartNew();

            try
            {
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                File.Copy(file.FullName, targetPath, overwrite: true);
                sw.Stop();
                transferTimeMs = sw.ElapsedMilliseconds;
            }
            catch
            {
                sw.Stop();
                transferTimeMs = -1;
            }

            _logger.Append(new LogEntry
            {
                Timestamp = DateTimeOffset.Now.ToString("o"),
                JobName = job.Name,
                SourceFile = file.FullName,
                TargetFile = targetPath,
                FileSize = file.Length,
                FileTransferTimeMs = transferTimeMs,
            });

            filesProcessed++;
            sizeProcessed += file.Length;
        }

        _stateTracker.Update(new StateEntry
        {
            Name = job.Name,
            LastActionTime = DateTimeOffset.Now,
            State = JobState.Inactive,
            TotalFilesEligible = totalFiles,
            TotalSize = totalSize,
            FilesRemaining = 0,
            SizeRemaining = 0,
        });
    }

    public void ExecuteAll() => throw new NotImplementedException();

    private static string BuildTargetPath(FileInfo file, DirectoryInfo sourceDir, string targetRoot)
    {
        string relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
        return Path.Combine(targetRoot, relativePath);
    }
}
