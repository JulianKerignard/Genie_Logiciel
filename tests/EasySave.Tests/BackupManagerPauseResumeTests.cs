using EasyLog;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class BackupManagerPauseResumeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly string _logDir;
    private readonly string _dataDir;

    public BackupManagerPauseResumeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bm-pause-tests-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_tempDir, "source");
        _targetDir = Path.Combine(_tempDir, "target");
        _logDir = Path.Combine(_tempDir, "logs");
        _dataDir = Path.Combine(_tempDir, "data");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_targetDir);
        Directory.CreateDirectory(_logDir);
        Directory.CreateDirectory(_dataDir);

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            LogDirectory = _logDir,
            StateFilePath = Path.Combine(_dataDir, "state.json"),
            JobsFilePath = Path.Combine(_dataDir, "jobs.json"),
        }));
        AppConfig.Load(configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BackupManager CreateManager(IDailyLogger? logger = null)
    {
        return new BackupManager(
            logger ?? new JsonDailyLogger(_logDir),
            new FullBackupStrategy(),
            new DifferentialBackupStrategy(),
            StateTracker.Instance,
            JobRepository.Instance,
            new NoOpEncryptionService(),
            Array.Empty<string>());
    }

    private void SeedJob(string name, BackupType type)
    {
        JobRepository.Instance.Save(new List<BackupJob>
        {
            new() { Name = name, SourcePath = _sourceDir, TargetPath = _targetDir, Type = type },
        });
    }

    private static StateEntry ReadStateEntry(string stateFile, string jobName)
    {
        var json = File.ReadAllText(stateFile);
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<StateEntry>>(json)!;
        return entries.Single(e => e.Name == jobName);
    }

    // Cancels the BackupManager once a given file count has been logged. Lets the
    // tests drive cancellation deterministically at a specific file boundary
    // without relying on wall-clock timing.
    private sealed class CancelAfterNthFileLogger : IDailyLogger
    {
        private readonly CancellationTokenSource _cts;
        private readonly int _afterCount;
        public int Calls { get; private set; }

        public CancelAfterNthFileLogger(CancellationTokenSource cts, int afterCount)
        {
            _cts = cts;
            _afterCount = afterCount;
        }

        public void Append(LogEntry entry)
        {
            Calls++;
            if (Calls >= _afterCount) _cts.Cancel();
        }
    }

    [Fact]
    public void ExecuteJob_TokenCancelledMidLoop_StopsAtFileBoundaryAndStateIsPaused()
    {
        // Five files: cancellation fires after the second file is logged, so the
        // remaining three must stay untouched and FilesRemaining must reflect them.
        for (int i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(_sourceDir, $"file-{i}.txt"), $"content-{i}");
        SeedJob("pause-mid", BackupType.Full);

        using var cts = new CancellationTokenSource();
        var logger = new CancelAfterNthFileLogger(cts, afterCount: 2);
        var manager = CreateManager(logger);

        Assert.Throws<OperationCanceledException>(
            () => manager.ExecuteJob("pause-mid", resumeAfterPath: null, ct: cts.Token));

        // Two files copied, three remain.
        var copied = Directory.GetFiles(_targetDir).Length;
        Assert.Equal(2, copied);

        var entry = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "pause-mid");
        Assert.Equal(JobState.Paused, entry.State);
        Assert.Equal(3, entry.FilesRemaining);
    }

    [Fact]
    public void ExecuteJob_FullBackup_ResumeAfterPath_SkipsAlreadyCopiedFiles()
    {
        // Five files. Resuming after file-2.txt must copy only the three files
        // ordinal-strictly-after it (file-3.txt, file-4.txt, file-5.txt).
        for (int i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(_sourceDir, $"file-{i}.txt"), $"content-{i}");
        SeedJob("resume-full", BackupType.Full);

        var manager = CreateManager();
        var resumeAfter = Path.Combine(_sourceDir, "file-2.txt");

        manager.ExecuteJob("resume-full", resumeAfterPath: resumeAfter);

        Assert.Equal(3, Directory.GetFiles(_targetDir).Length);
        var entry = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "resume-full");
        Assert.Equal(JobState.Inactive, entry.State);
        Assert.Equal(0, entry.FilesRemaining);
    }

    [Fact]
    public void PauseThenResume_FullBackup_CopiesEachFileExactlyOnce()
    {
        // End-to-end pause/resume: cancel after 2 files, then resume from the
        // computed index. All 5 files must end up copied with no duplicates.
        for (int i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(_sourceDir, $"file-{i}.txt"), $"content-{i}");
        SeedJob("pause-resume", BackupType.Full);

        // First pass: cancel after 2 files.
        using (var cts = new CancellationTokenSource())
        {
            var logger = new CancelAfterNthFileLogger(cts, afterCount: 2);
            var manager = CreateManager(logger);
            Assert.Throws<OperationCanceledException>(
                () => manager.ExecuteJob("pause-resume", resumeAfterPath: null, ct: cts.Token));
        }

        var afterPause = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "pause-resume");
        // CurrentSource is the path of the last successfully copied file —
        // the path-based resume cursor that survives source mutations.
        var resumeAfter = afterPause.CurrentSource;

        // Second pass: resume from where we left off.
        var resumeManager = CreateManager();
        resumeManager.ExecuteJob("pause-resume", resumeAfterPath: resumeAfter);

        // Name-set check: verifies the *identity* of every copied file. A double
        // copy + a missed file would still total five but a missing name would
        // fail this assertion.
        var expected = Enumerable.Range(1, 5).Select(i => $"file-{i}.txt").OrderBy(n => n).ToArray();
        var actual = Directory.GetFiles(_targetDir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(expected, actual);

        var afterResume = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "pause-resume");
        Assert.Equal(JobState.Inactive, afterResume.State);
        Assert.Equal(0, afterResume.FilesRemaining);
    }

    [Fact]
    public void PauseThenResume_FullBackup_SourceFileDeletedBeforeResume_DoesNotSkipNextFile()
    {
        // Regression: prior to path-based cursor, the resume index was computed as
        // (TotalFilesEligible - FilesRemaining) = (5 - 3) = 2, then Skip(2) was
        // applied to the *re-scanned* eligible list. If a copied file (file-1) was
        // deleted between pause and resume, the new eligible list shrank to 4 and
        // Skip(2) silently skipped file-3, never copying it.
        for (int i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(_sourceDir, $"file-{i}.txt"), $"content-{i}");
        SeedJob("source-shrunk", BackupType.Full);

        // First pass: cancel after 2 files (file-1, file-2 land in target).
        using (var cts = new CancellationTokenSource())
        {
            var logger = new CancelAfterNthFileLogger(cts, afterCount: 2);
            var manager = CreateManager(logger);
            Assert.Throws<OperationCanceledException>(
                () => manager.ExecuteJob("source-shrunk", resumeAfterPath: null, ct: cts.Token));
        }
        Assert.Equal(2, Directory.GetFiles(_targetDir).Length);

        // Operator deletes the first source file before clicking Resume.
        File.Delete(Path.Combine(_sourceDir, "file-1.txt"));

        var afterPause = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "source-shrunk");
        var resumeAfter = afterPause.CurrentSource;

        // Resume must still pick up file-3, file-4, file-5 (everything ordinal-after
        // the persisted cursor "file-2.txt"), regardless of the source shrinking.
        var resumeManager = CreateManager();
        resumeManager.ExecuteJob("source-shrunk", resumeAfterPath: resumeAfter);

        var copiedNames = Directory.GetFiles(_targetDir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Contains("file-3.txt", copiedNames);
        Assert.Contains("file-4.txt", copiedNames);
        Assert.Contains("file-5.txt", copiedNames);
    }

    // Signals a TaskCompletionSource the first time a JobPaused log entry
    // arrives, so the test knows BackupManager is parked on its pause gate
    // without resorting to Thread.Sleep / Task.Delay timing probes.
    private sealed class JobPausedSignalLogger : IDailyLogger
    {
        private readonly TaskCompletionSource _onJobPaused = new();
        public Task Paused => _onJobPaused.Task;

        public void Append(LogEntry entry)
        {
            if (entry.EventType == LogEvent.JobPaused)
                _onJobPaused.TrySetResult();
        }
    }

    [Fact]
    public async Task ExecuteJob_PauseGateReset_StateBecomesPausedThenResumes()
    {
        // V3 path: ExecuteJob receives a ManualResetEventSlim that the
        // orchestrator's IJobController.Pause / PauseAll reset. The runner
        // must transition state.json to Paused at the next file boundary,
        // wait until the gate is signaled, then continue and finish
        // Inactive.
        for (int i = 1; i <= 4; i++)
            File.WriteAllText(Path.Combine(_sourceDir, $"file-{i}.txt"), $"content-{i}");
        SeedJob("gated", BackupType.Full);

        var logger = new JobPausedSignalLogger();
        var manager = CreateManager(logger);

        // Gate starts CLOSED so the first file boundary already stalls.
        using var pauseGate = new ManualResetEventSlim(initialState: false);
        // Hard ceiling so a regression that leaves the runner parked
        // forever fails the test instead of hanging the suite.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execution = Task.Run(() => manager.ExecuteJob(
            "gated", resumeAfterPath: null, pauseGate: pauseGate, ct: cts.Token));

        // Wait for the JobPaused log entry — proves the runner is parked
        // on the gate at the file boundary.
        await logger.Paused.WaitAsync(TimeSpan.FromSeconds(2));

        var paused = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "gated");
        Assert.Equal(JobState.Paused, paused.State);
        // Counters are preserved while paused (job has not progressed yet).
        Assert.Equal(4, paused.FilesRemaining);

        // Release the gate and let the job complete normally.
        pauseGate.Set();
        await execution;

        var finished = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "gated");
        Assert.Equal(JobState.Inactive, finished.State);
        Assert.Equal(0, finished.FilesRemaining);
        Assert.Equal(4, Directory.GetFiles(_targetDir).Length);
    }

    [Fact]
    public async Task ExecuteJob_PauseGateAndStopTogether_StateBecomesInactive()
    {
        // V3 semantics: with a pauseGate supplied, an OperationCanceledException
        // means Stop (not Pause). The job ends Inactive, not Paused — that's
        // the contract IParallelBackupOrchestrator.Stop relies on so the v2
        // pause-as-cancel behaviour stays intact for the adapter callers.
        for (int i = 1; i <= 4; i++)
            File.WriteAllText(Path.Combine(_sourceDir, $"file-{i}.txt"), $"content-{i}");
        SeedJob("gated-stop", BackupType.Full);

        var logger = new JobPausedSignalLogger();
        var manager = CreateManager(logger);

        using var pauseGate = new ManualResetEventSlim(initialState: false);
        using var cts = new CancellationTokenSource();

        var execution = Task.Run(() =>
            Assert.Throws<OperationCanceledException>(() => manager.ExecuteJob(
                "gated-stop", resumeAfterPath: null, pauseGate: pauseGate, ct: cts.Token)));

        await logger.Paused.WaitAsync(TimeSpan.FromSeconds(2));

        // Stop while paused. The token-aware Wait inside BackupManager
        // throws OCE and the catch must land Inactive, not Paused.
        cts.Cancel();
        await execution;

        var stopped = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "gated-stop");
        Assert.Equal(JobState.Inactive, stopped.State);
    }
}
