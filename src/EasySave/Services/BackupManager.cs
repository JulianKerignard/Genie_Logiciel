using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

/// <summary>
/// Orchestrates the lifecycle of backup jobs: CRUD operations against the
/// persistent store, execution using the strategy pattern, real-time state
/// updates, and per-file logging. v2.0 lifts the v1.0 5-job cap (see
/// docs/EasySave_v2_0_Repartition_Taches.md — Phase 2 Chloé: "Supprimer la
/// limite de 5 jobs"; Phase 4: "[Recette V2] 6+ jobs acceptés").
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
    private readonly HashSet<string> _priorityExtensions;
    private readonly IBigFileGate? _bigFileGate;
    private readonly IPriorityGate? _priorityGate;

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
    /// <param name="bigFileGate">
    /// V3 gate that serializes the transfer of files >= its threshold across
    /// every job running in parallel. Pass <c>null</c> in tests or v1/v2
    /// hosts that do not run jobs in parallel — every file is then copied
    /// without any cross-job coordination.
    /// </param>
    public BackupManager(
        IDailyLogger logger,
        IBackupStrategy fullStrategy,
        IBackupStrategy diffStrategy,
        StateTracker stateTracker,
        JobRepository jobRepository,
        IEncryptionService encryption,
        IEnumerable<string> encryptedExtensions,
        IBigFileGate? bigFileGate = null,
        IPriorityGate? priorityGate = null,
        IEnumerable<string>? priorityExtensions = null)
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
        _priorityExtensions = new HashSet<string>(
            priorityExtensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _bigFileGate = bigFileGate;
        _priorityGate = priorityGate;
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
        _stateTracker.Remove(name);
    }

    /// <summary>Returns the current list of configured jobs, read from disk.</summary>
    public IReadOnlyList<BackupJob> ListJobs() => _jobRepository.Load();

    /// <summary>
    /// Runs a single job by name. Copies eligible files according to the job's
    /// strategy, logs each file (success or failure), and updates the state
    /// tracker at start, per file, and on completion.
    /// </summary>
    /// <param name="name">Name of the job to execute (case-insensitive).</param>
    /// <param name="resumeAfterPath">
    /// Resume a paused Full-backup job at the eligible file whose full path is
    /// ordinal-strictly-greater than this value. Null (default) means a fresh
    /// run from the first file. Differential jobs ignore this parameter — the
    /// re-scan already yields only remaining files because copied files now
    /// have matching mtime. Path-based tracking (vs. an index) survives source
    /// mutations between pause and resume: a file added before the cursor is
    /// still picked up on the next clean run, and removed files no longer
    /// shift the cursor and silently skip the wrong file.
    /// </param>
    /// <param name="ct">
    /// Token that stops the job at the next file boundary (atomically —
    /// the file in progress is not interrupted). When cancelled the state
    /// is left as <see cref="JobState.Paused"/> so the caller can resume later.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no job with that name exists.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the source directory does not exist.</exception>
    public void ExecuteJob(string name, string? resumeAfterPath = null, ManualResetEventSlim? pauseGate = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var jobs = _jobRepository.Load();
        var job = jobs.FirstOrDefault(j => j.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                  ?? throw new KeyNotFoundException(name);

        RunJob(job, resumeAfterPath, pauseGate, ct);
    }

    /// <summary>
    /// Runs every configured job sequentially. A failure on one job is logged
    /// to <see cref="Console.Error"/> but does not stop the following jobs.
    /// </summary>
    public void ExecuteAll()
    {
        foreach (var job in _jobRepository.Load())
        {
            try { RunJob(job, resumeAfterPath: null, pauseGate: null, CancellationToken.None); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[BackupManager] Job '{job.Name}' failed: {ex.Message}");
            }
        }
    }

    private void RunJob(BackupJob job, string? resumeAfterPath, ManualResetEventSlim? pauseGate, CancellationToken ct)
    {
        var sourceDir = new DirectoryInfo(job.SourcePath);
        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException($"Source not found: {job.SourcePath}");

        var strategy = job.Type == BackupType.Full ? _fullStrategy : _diffStrategy;

        // Sort by full path with an ordinal comparer so the eligible list order
        // is deterministic across runs. DirectoryInfo.GetFiles makes no
        // ordering guarantee; without that, the resume cursor (an ordinal path)
        // could skip files on filesystems that re-order between calls.
        var eligible = sourceDir
            .GetFiles("*", SearchOption.AllDirectories)
            .Select(f => (file: f, target: GetTargetPath(job, sourceDir, f)))
            .Where(x => strategy.ShouldCopy(x.file, x.target))
            .OrderBy(x => x.file.FullName, StringComparer.Ordinal)
            .ToList();

        // Differential jobs re-scan from scratch: copied files no longer appear
        // in eligible (mtime matches source). Full jobs that resume drop every
        // entry up to and including the last file persisted in state.json
        // (state.CurrentSource on the previous tick). Path-based filtering is
        // robust to source mutations between pause and resume, where an index-
        // based cursor would point at the wrong file.
        var toCopy = job.Type == BackupType.Full && !string.IsNullOrEmpty(resumeAfterPath)
            ? eligible.Where(x => StringComparer.Ordinal.Compare(x.file.FullName, resumeAfterPath) > 0).ToList()
            : eligible;

        // CdC V3: priority-extension files copy before non-priority files
        // inside the SAME job. The cross-job barrier (PriorityGate.Wait)
        // already prevents non-priority files of any job from starting
        // while priorities are pending anywhere; this in-job reordering
        // is a pure optimization that lets the gate clear faster (each
        // job drains its priorities first instead of interleaving).
        // When no priority extension is configured, all files are non-
        // priority and the OrderBy is a stable no-op on the existing
        // ordinal sort.
        toCopy = toCopy
            .OrderByDescending(x => IsPriority(x.file))
            .ThenBy(x => x.file.FullName, StringComparer.Ordinal)
            .ToList();
        var priorityFilesRemaining = toCopy.Count(x => IsPriority(x.file));

        var state = new StateEntry
        {
            Name = job.Name,
            State = JobState.Active,
            LastActionTime = DateTimeOffset.Now,
            TotalFilesEligible = eligible.Count,
            TotalSize = eligible.Sum(x => x.file.Length),
            FilesRemaining = toCopy.Count,
            SizeRemaining = toCopy.Sum(x => x.file.Length),
        };
        _stateTracker.Update(state);

        // Register the job's priority count with the cross-job gate before
        // the loop starts so a parallel non-priority file from another job
        // sees the pending priorities and waits.
        _priorityGate?.RegisterJob(job.Name, priorityFilesRemaining);

        bool succeeded = false;
        bool paused = false;
        try
        {
            foreach (var (file, targetPath) in toCopy)
            {
                // Check at file boundary — never mid-copy — so the target file is
                // never left in a partial state.
                ct.ThrowIfCancellationRequested();

                bool isPriority = IsPriority(file);

                // CdC V3 hard rule: « Aucune sauvegarde d'un fichier non
                // prioritaire ne peut se faire tant qu'il y a des
                // extensions prioritaires en attente sur au moins un
                // travail. » The PriorityGate is signaled when every
                // registered job has zero priority files left.
                if (!isPriority && _priorityGate is not null)
                    _priorityGate.WaitForNonPriorityWindow(ct);

                // V3 PauseGate: if a controller (IJobController.Pause /
                // PauseAll, business-software watcher, remote console) has
                // reset the gate, stall here until it gets signaled again.
                // The wait is token-aware so a concurrent Stop unblocks it
                // with an OperationCanceledException rather than a hung
                // worker thread. Transitions are logged and reflected in
                // state.json so the GUI and remote consoles see the pause.
                if (pauseGate is not null && !pauseGate.IsSet)
                {
                    state.State = JobState.Paused;
                    state.LastActionTime = DateTimeOffset.Now;
                    _stateTracker.Update(state);
                    _logger.Append(new LogEntry
                    {
                        Timestamp = DateTimeOffset.Now.ToString("o"),
                        JobName = job.Name,
                        SourceFile = string.Empty,
                        TargetFile = string.Empty,
                        FileSize = 0,
                        FileTransferTimeMs = 0,
                        EventType = LogEvent.JobPaused,
                    });

                    pauseGate.Wait(ct);

                    state.State = JobState.Active;
                    state.LastActionTime = DateTimeOffset.Now;
                    _stateTracker.Update(state);
                    _logger.Append(new LogEntry
                    {
                        Timestamp = DateTimeOffset.Now.ToString("o"),
                        JobName = job.Name,
                        SourceFile = string.Empty,
                        TargetFile = string.Empty,
                        FileSize = 0,
                        FileTransferTimeMs = 0,
                        EventType = LogEvent.JobResumed,
                    });
                }

                FileHelpers.EnsureDirectoryExists(targetPath);

                var (transferMs, encryptionMs) = ProcessFile(file, targetPath, ct);

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

                // Tick the cross-job barrier AFTER the copy succeeds so a
                // failed transfer doesn't prematurely unblock waiters.
                if (isPriority)
                    _priorityGate?.MarkPriorityFileDone(job.Name);
            }
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            // v2 callers cancel the token when they want a Pause (the
            // adapter then restarts the job from the resume cursor). v3
            // callers (BackupManagerJobRunner) cancel the token only for
            // Stop and use the dedicated PauseGate for Pause, so an OCE on
            // that path means "stop" and must leave the job Inactive, not
            // Paused. Differentiate on whether a pauseGate was supplied.
            paused = pauseGate is null;
            throw;
        }
        finally
        {
            // Unregister from the priority gate regardless of how the loop
            // exited: leftover priorities on a stopped / failed job would
            // otherwise hold every other job's non-priority files hostage.
            _priorityGate?.UnregisterJob(job.Name);

            // Always transition the state. On pause, preserve progress counters
            // so the adapter can compute the resume index from FilesRemaining.
            try
            {
                state.State = paused ? JobState.Paused : JobState.Inactive;
                if (!paused)
                {
                    state.FilesRemaining = 0;
                    state.SizeRemaining = 0;
                    state.CurrentSource = string.Empty;
                    state.CurrentTarget = string.Empty;
                }
                state.LastActionTime = DateTimeOffset.Now;
                _stateTracker.Update(state);
            }
            catch when (!succeeded && !paused)
            {
                // On the non-pause failure path, do not replace the in-flight
                // exception with a state-writer failure.
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
    //
    // The big-file gate (v3, optional) serializes the transfer of files >=
    // its threshold across every job running in parallel — prevents two
    // multi-GB copies from saturating disk/network simultaneously. Below
    // the threshold the gate hands back a no-op handle so small files copy
    // freely with zero overhead.
    private (long transferMs, long? encryptionMs) ProcessFile(FileInfo file, string targetPath, CancellationToken ct)
    {
        // Acquire synchronously: ProcessFile is sync and called from a
        // worker thread (Task.Run inside BackupManagerAdapter), so
        // GetAwaiter().GetResult() does not risk a UI-thread deadlock.
        // The gate is null in v1/v2 hosts where there is no parallelism.
        using var gateHandle = _bigFileGate is null
            ? null
            : _bigFileGate.AcquireAsync(file.Length, ct).GetAwaiter().GetResult();

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

    // True when the file's extension is configured as a CdC V3 priority
    // extension. Used to order files inside a job (priorities first) and
    // to count the per-job priority budget registered with the
    // cross-job PriorityGate.
    private bool IsPriority(FileInfo file)
    {
        if (_priorityExtensions.Count == 0) return false;
        var ext = file.Extension;
        return !string.IsNullOrEmpty(ext) && _priorityExtensions.Contains(ext);
    }
}
