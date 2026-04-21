using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Manages backup jobs: CRUD + execution with logging and state tracking.
public sealed class BackupManager
{
    private const int MaxJobs = 5;

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

    public void AddJob(BackupJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.TargetPath);

        var jobs = _jobRepository.Load().ToList();

        if (jobs.Count >= MaxJobs)
            throw new InvalidOperationException($"error.max_jobs: Maximum {MaxJobs} jobs allowed.");

        if (jobs.Any(j => j.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"error.duplicate_job: Job '{job.Name}' already exists.");

        jobs.Add(job);
        _jobRepository.Save(jobs);
    }

    public void RemoveJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var jobs = _jobRepository.Load().ToList();
        var index = jobs.FindIndex(j => j.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
            throw new KeyNotFoundException(name);

        jobs.RemoveAt(index);
        _jobRepository.Save(jobs);
    }

    public IReadOnlyList<BackupJob> ListJobs() => _jobRepository.Load();

    public void ExecuteJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var jobs = _jobRepository.Load();
        var job = jobs.FirstOrDefault(j => j.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                  ?? throw new KeyNotFoundException(name);

        RunJob(job);
    }

    public void ExecuteAll()
    {
        foreach (var job in _jobRepository.Load())
        {
            try { RunJob(job); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[BackupManager] Job '{job.Name}' failed: {ex.Message}");
            }
        }
    }

    private void RunJob(BackupJob job)
    {
        var sourceDir = new DirectoryInfo(job.SourcePath);
        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException($"Source not found: {job.SourcePath}");

        var strategy = job.Type == BackupType.Full ? _fullStrategy : _diffStrategy;

        var eligible = sourceDir
            .GetFiles("*", SearchOption.AllDirectories)
            .Select(f => (file: f, target: GetTargetPath(job, sourceDir, f)))
            .Where(x => strategy.ShouldCopy(x.file, x.target))
            .ToList();

        var state = new StateEntry
        {
            Name = job.Name,
            State = JobState.Active,
            LastActionTime = DateTimeOffset.Now,
            TotalFilesEligible = eligible.Count,
            TotalSize = eligible.Sum(x => x.file.Length),
            FilesRemaining = eligible.Count,
            SizeRemaining = eligible.Sum(x => x.file.Length)
        };
        _stateTracker.Update(state);

        foreach (var (file, targetPath) in eligible)
        {
            FileHelpers.EnsureDirectoryExists(targetPath);

            var sw = Stopwatch.StartNew();
            long transferMs;
            try
            {
                File.Copy(file.FullName, targetPath, overwrite: true);
                sw.Stop();
                transferMs = sw.ElapsedMilliseconds;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sw.Stop();
                transferMs = -1;
            }

            _logger.Append(new LogEntry
            {
                Timestamp = DateTimeOffset.Now.ToString("o"),
                JobName = job.Name,
                SourceFile = file.FullName,
                TargetFile = targetPath,
                FileSize = file.Length,
                FileTransferTimeMs = transferMs
            });

            state.FilesRemaining--;
            state.SizeRemaining -= file.Length;
            state.CurrentSource = file.FullName;
            state.CurrentTarget = targetPath;
            state.LastActionTime = DateTimeOffset.Now;
            _stateTracker.Update(state);
        }

        state.State = JobState.Inactive;
        state.FilesRemaining = 0;
        state.SizeRemaining = 0;
        state.CurrentSource = string.Empty;
        state.CurrentTarget = string.Empty;
        state.LastActionTime = DateTimeOffset.Now;
        _stateTracker.Update(state);
    }

    private static string GetTargetPath(BackupJob job, DirectoryInfo sourceDir, FileInfo file)
    {
        var relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
        return Path.Combine(job.TargetPath, relativePath);
    }
}
