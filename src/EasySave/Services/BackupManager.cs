using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

/// <summary>
/// Orchestrates the lifecycle of backup jobs: CRUD operations against the
/// persistent store, execution using the strategy pattern, real-time state
/// updates, and per-file logging. v2.0 lifts the v1.0 5-job cap; the number
/// of jobs is now unlimited.
/// </summary>
public sealed class BackupManager
{
    private readonly IDailyLogger _logger;
    private readonly IBackupStrategy _fullStrategy;
    private readonly IBackupStrategy _diffStrategy;
    private readonly StateTracker _stateTracker;
    private readonly JobRepository _jobRepository;
    private readonly IEncryptionService _encryption;
    private readonly HashSet<string> _encryptedExtensions;

    /// <summary>
    /// Wires the manager with its dependencies. All parameters are required;
    /// null arguments throw <see cref="ArgumentNullException"/>.
    /// </summary>
    /// <param name="logger">Daily log writer, shared across jobs.</param>
    /// <param name="fullStrategy">Strategy used for <see cref="BackupType.Full"/> jobs.</param>
    /// <param name="diffStrategy">Strategy used for <see cref="BackupType.Differential"/> jobs.</param>
    /// <param name="stateTracker">Singleton state writer persisting to <c>state.json</c>.</param>
    /// <param name="jobRepository">Singleton repository persisting to <c>jobs.json</c>.</param>
    /// <param name="encryption">Encryption side-channel; pass a <see cref="NoOpEncryptionService"/> to disable encryption.</param>
    /// <param name="encryptedExtensions">File extensions (lowercase, leading dot) that must go through <paramref name="encryption"/> instead of a plain copy. Pass an empty list to disable.</param>
    public BackupManager(
        IDailyLogger logger,
        IBackupStrategy fullStrategy,
        IBackupStrategy diffStrategy,
        StateTracker stateTracker,
        JobRepository jobRepository,
        IEncryptionService encryption,
        IEnumerable<string> encryptedExtensions)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fullStrategy);
        ArgumentNullException.ThrowIfNull(diffStrategy);
        ArgumentNullException.ThrowIfNull(stateTracker);
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(encryptedExtensions);

        _logger = logger;
        _fullStrategy = fullStrategy;
        _diffStrategy = diffStrategy;
        _stateTracker = stateTracker;
        _jobRepository = jobRepository;
        _encryption = encryption;
        _encryptedExtensions = new HashSet<string>(encryptedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers a new backup job and persists the updated list to disk.
    /// The job name is matched case-insensitively for duplicate detection.
    /// </summary>
    /// <param name="job">Job definition with non-empty Name, SourcePath, and TargetPath.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="job"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when any of Name/SourcePath/TargetPath is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a job with the same name already exists (key <c>error.duplicate_job</c>).
    /// </exception>
    public void AddJob(BackupJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.TargetPath);

        var jobs = _jobRepository.Load().ToList();

        if (jobs.Any(j => j.Name.Equals(job.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"error.duplicate_job: Job '{job.Name}' already exists.");

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
    /// Runs every configured job sequentially. A failure on one job is logged
    /// to <see cref="Console.Error"/> but does not stop the following jobs.
    /// </summary>
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

        bool succeeded = false;
        try
        {
            foreach (var (file, targetPath) in eligible)
            {
                FileHelpers.EnsureDirectoryExists(targetPath);

                var (transferMs, encryptionMs) = ProcessFile(file, targetPath);

                _logger.Append(new LogEntry
                {
                    Timestamp = DateTimeOffset.Now.ToString("o"),
                    JobName = job.Name,
                    SourceFile = file.FullName,
                    TargetFile = targetPath,
                    FileSize = file.Length,
                    FileTransferTimeMs = transferMs,
                    EncryptionTimeMs = encryptionMs,
                });

                state.FilesRemaining--;
                state.SizeRemaining -= file.Length;
                state.CurrentSource = file.FullName;
                state.CurrentTarget = targetPath;
                state.LastActionTime = DateTimeOffset.Now;
                _stateTracker.Update(state);
            }
            succeeded = true;
        }
        finally
        {
            // Always flip the state back to Inactive, even if the loop above
            // throws (logger I/O error, state writer failure, permission glitch).
            // The conditional catch below only swallows on the failure path so
            // the in-flight exception is not masked by a follow-up state-writer
            // failure; on the success path any state-writer error propagates.
            try
            {
                state.State = JobState.Inactive;
                state.FilesRemaining = 0;
                state.SizeRemaining = 0;
                state.CurrentSource = string.Empty;
                state.CurrentTarget = string.Empty;
                state.LastActionTime = DateTimeOffset.Now;
                _stateTracker.Update(state);
            }
            catch when (!succeeded)
            {
                // Failure path only: do not replace the in-flight exception.
            }
        }
    }

    private static string GetTargetPath(BackupJob job, DirectoryInfo sourceDir, FileInfo file)
    {
        var relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
        return Path.Combine(job.TargetPath, relativePath);
    }

    // Routes a single file either through the encryption side-channel (if its
    // extension is in the configured list) or through a plain File.Copy.
    // Returns the two times to log: file transfer (always set, negative on
    // failure) and encryption (null when no encryption was attempted).
    private (long transferMs, long? encryptionMs) ProcessFile(FileInfo file, string targetPath)
    {
        if (ShouldEncrypt(file.Name))
        {
            var sw = Stopwatch.StartNew();
            var result = _encryption.Encrypt(file.FullName, targetPath);
            sw.Stop();

            if (result.Success)
            {
                AlignTargetMtime(file, targetPath);
            }

            // CryptoSoft writes the encrypted bytes to targetPath itself, so the
            // wall-clock duration of the Encrypt call doubles as the file
            // transfer time for v1.0 log consumers.
            long transferMs = result.Success ? sw.ElapsedMilliseconds : -1;
            return (transferMs, result.EncryptionTimeMs);
        }

        var copyTimer = Stopwatch.StartNew();
        try
        {
            File.Copy(file.FullName, targetPath, overwrite: true);
            copyTimer.Stop();
            AlignTargetMtime(file, targetPath);
            return (copyTimer.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (-1, null);
        }
    }

    // Carries the source file's LastWriteTimeUtc onto the target so the next
    // run of DifferentialBackupStrategy can decide based on dates alone.
    // This is what lets diff backups work for encrypted files (whose size
    // never matches the source) without storing a parallel history file.
    private static void AlignTargetMtime(FileInfo source, string targetPath)
    {
        try
        {
            File.SetLastWriteTimeUtc(targetPath, source.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy already succeeded; failing to stamp the mtime only
            // means the next diff run will re-copy this file. Better to keep
            // going than to fail the whole job over a metadata write.
        }
    }

    private bool ShouldEncrypt(string fileName)
    {
        if (_encryptedExtensions.Count == 0) return false;
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && _encryptedExtensions.Contains(ext);
    }
}
