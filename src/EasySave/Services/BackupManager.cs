using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Manages backup jobs: CRUD + execution with logging and state tracking.
public class BackupManager
{
    private const int MaxJobs = 5;

    private readonly IDailyLogger _logger;
    private readonly IBackupStrategy _fullStrategy;
    private readonly IBackupStrategy _diffStrategy;
    private readonly StateTracker _stateTracker;
    private readonly JobRepository _jobRepository;
    private readonly List<BackupJob> _jobs;

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
        _jobs = new List<BackupJob>(_jobRepository.Load());
    }

    public void AddJob(BackupJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (_jobs.Count >= MaxJobs)
            throw new InvalidOperationException($"Maximum {MaxJobs} jobs allowed.");

        if (_jobs.Any(j => j.Name == job.Name))
            throw new InvalidOperationException($"Job '{job.Name}' already exists.");

        _jobs.Add(job);
        _jobRepository.Save(_jobs);
    }

    public void RemoveJob(string name) => throw new NotImplementedException();

    public IReadOnlyList<BackupJob> ListJobs() => _jobs.AsReadOnly();

    public void ExecuteJob(string name)
    {
        var job = _jobs.FirstOrDefault(j => j.Name == name)
            ?? throw new InvalidOperationException($"Job '{name}' not found.");

        var strategy = job.Type == BackupType.Full ? _fullStrategy : _diffStrategy;
        var sourceDir = new DirectoryInfo(job.SourcePath);

        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {job.SourcePath}");

        var allFiles = sourceDir.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        var filesToCopy = allFiles
            .Select(f => (File: f, Target: BuildTargetPath(f, sourceDir, job.TargetPath)))
            .Where(pair => strategy.ShouldCopy(pair.File, pair.Target))
            .ToList();

        int totalFiles = filesToCopy.Count;
        long totalSize = filesToCopy.Sum(pair => pair.File.Length);

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

        foreach (var (file, targetPath) in filesToCopy)
        {

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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
