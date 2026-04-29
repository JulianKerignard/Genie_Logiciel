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
            () => manager.ExecuteJob("pause-mid", startFromIndex: 0, cts.Token));

        // Two files copied, three remain.
        var copied = Directory.GetFiles(_targetDir).Length;
        Assert.Equal(2, copied);

        var entry = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "pause-mid");
        Assert.Equal(JobState.Paused, entry.State);
        Assert.Equal(3, entry.FilesRemaining);
    }

    [Fact]
    public void ExecuteJob_FullBackup_StartFromIndex_SkipsAlreadyCopiedFiles()
    {
        // Five files. Resuming with startFromIndex = 2 must copy only the last
        // three (indices 2, 3, 4 in the eligible list).
        for (int i = 1; i <= 5; i++)
            File.WriteAllText(Path.Combine(_sourceDir, $"file-{i}.txt"), $"content-{i}");
        SeedJob("resume-full", BackupType.Full);

        var manager = CreateManager();

        manager.ExecuteJob("resume-full", startFromIndex: 2);

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
                () => manager.ExecuteJob("pause-resume", startFromIndex: 0, cts.Token));
        }

        var afterPause = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "pause-resume");
        int resumeFrom = afterPause.TotalFilesEligible - afterPause.FilesRemaining;

        // Second pass: resume from where we left off.
        var resumeManager = CreateManager();
        resumeManager.ExecuteJob("pause-resume", startFromIndex: resumeFrom);

        Assert.Equal(5, Directory.GetFiles(_targetDir).Length);
        var afterResume = ReadStateEntry(Path.Combine(_dataDir, "state.json"), "pause-resume");
        Assert.Equal(JobState.Inactive, afterResume.State);
        Assert.Equal(0, afterResume.FilesRemaining);
    }
}
