using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

/// <summary>
/// Orchestrates the lifecycle of backup jobs: CRUD operations against the
/// persistent store, execution using the strategy pattern, real-time state
/// updates, and per-file logging. Enforces the v1.0 cahier limit of 5 jobs.
/// </summary>
public sealed class BackupManager
{
    /// <summary>Maximum number of jobs allowed at any time (cahier v1.0).</summary>
    private const int MaxJobs = BackupLimits.MaxJobs;

    private readonly IDailyLogger _logger;
    private readonly IBackupStrategy _fullStrategy;
    private readonly IBackupStrategy _diffStrategy;
    private readonly StateTracker _stateTracker;
    private readonly JobRepository _jobRepository;

    /// <summary>
    /// Wires the manager with its dependencies. All parameters are required;
    /// null arguments throw <see cref="ArgumentNullException"/>.
    /// </summary>
    /// <param name="logger">Daily log writer, shared across jobs.</param>
    /// <param name="fullStrategy">Strategy used for <see cref="BackupType.Full"/> jobs.</param>
    /// <param name="diffStrategy">Strategy used for <see cref="BackupType.Differential"/> jobs.</param>
    /// <param name="stateTracker">Singleton state writer persisting to <c>state.json</c>.</param>
    /// <param name="jobRepository">Singleton repository persisting to <c>jobs.json</c>.</param>
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

    /// <summary>
    /// Registers a new backup job and persists the updated list to disk.
    /// The job name is matched case-insensitively for duplicate detection.
    /// </summary>
    /// <param name="job">Job definition with non-empty Name, SourcePath, and TargetPath.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="job"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when any of Name/SourcePath/TargetPath is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the repository already holds 5 jobs (key <c>error.max_jobs</c>)
    /// or when a job with the same name exists (key <c>error.duplicate_job</c>).
    /// </exception>
    public void AddJob(BackupJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.TargetPath);

        var jobs = _jobRepository.Load().ToList();

        if (jobs.Count >= MaxJobs)
            throw new InvalidOperationException("error.max_jobs");

        if (jobs.Any(j => j.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("error.duplicate_job");

        jobs.Add(job);
        _jobRepository.Save(jobs);
    }

    /// <summary>
    /// Removes the job matching <paramref name="name"/> (case-insensitive) and
    /// persists the updated list to disk.
    /// </summary>
    /// <param name="name">Name of the job to remove.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no job with that name exists.</exception>
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

    /// <summary>Returns the current list of configured jobs, read from disk.</summary>
    public IReadOnlyList<BackupJob> ListJobs() => _jobRepository.Load();

    /// <summary>
    /// Runs a single job by name. Copies eligible files according to the job's
    /// strategy, logs each file (success or failure), and updates the state
    /// tracker at start, per file, and on completion.
    /// </summary>
    /// <param name="name">Name of the job to execute (case-insensitive).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no job with that name exists.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the source directory does not exist.</exception>
    public void ExecuteJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var jobs = _jobRepository.Load();
        var job = jobs.FirstOrDefault(j => j.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                  ?? throw new KeyNotFoundException(name);

        RunJob(job);
    }

    /// <summary>
    /// Runs every configured job sequentially. Failures on individual jobs do not
    /// stop the loop; only IO/permission/config errors known to be safe to skip
    /// are caught. Programmer errors (NullReference, OutOfMemory, etc.) propagate.
    /// </summary>
    /// <remarks>
    /// Diagnostic output is written to <see cref="Console.Error"/> as a fallback
    /// for v1.x. v2 will replace this with an explicit failure-callback parameter
    /// so the service layer no longer writes to the console.
    /// </remarks>
    public void ExecuteAll()
    {
        foreach (var job in _jobRepository.Load())
        {
            try { RunJob(job); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // DirectoryNotFoundException is a subclass of IOException, already covered.
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
