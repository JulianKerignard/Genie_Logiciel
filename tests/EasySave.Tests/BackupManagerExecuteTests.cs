using EasyLog;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class BackupManagerExecuteTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly string _logDir;
    private readonly string _dataDir;

    public BackupManagerExecuteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bm-exec-tests-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_tempDir, "source");
        _targetDir = Path.Combine(_tempDir, "target");
        _logDir = Path.Combine(_tempDir, "logs");
        _dataDir = Path.Combine(_tempDir, "data");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_targetDir);
        Directory.CreateDirectory(_logDir);
        Directory.CreateDirectory(_dataDir);

        // Point AppConfig to our temp directories
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

    private BackupManager CreateManager(IBackupStrategy fullStrategy, IBackupStrategy diffStrategy)
    {
        var logger = new JsonDailyLogger(_logDir);
        return new BackupManager(logger, fullStrategy, diffStrategy, StateTracker.Instance, JobRepository.Instance);
    }

    private void SeedJob(string name, BackupType type)
    {
        var job = new BackupJob
        {
            Name = name,
            SourcePath = _sourceDir,
            TargetPath = _targetDir,
            Type = type,
        };
        JobRepository.Instance.Save(new List<BackupJob> { job });
    }

    [Fact]
    public void ExecuteJob_FullStrategy_CopiesAllFiles_AndLogsEach()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "a.txt"), "aaa");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "sub"));
        File.WriteAllText(Path.Combine(_sourceDir, "sub", "b.txt"), "bbb");

        SeedJob("full-test", BackupType.Full);
        var manager = CreateManager(new FullBackupStrategy(), new DifferentialBackupStrategy());

        manager.ExecuteJob("full-test");

        Assert.True(File.Exists(Path.Combine(_targetDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_targetDir, "sub", "b.txt")));
        Assert.Equal("aaa", File.ReadAllText(Path.Combine(_targetDir, "a.txt")));
        Assert.Equal("bbb", File.ReadAllText(Path.Combine(_targetDir, "sub", "b.txt")));

        // Check that log entries were created
        var logFiles = Directory.GetFiles(_logDir, "*.json");
        Assert.Single(logFiles);
    }

    [Fact]
    public void ExecuteJob_DiffStrategy_CopiesOnlyModified()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "unchanged.txt"), "same");
        File.WriteAllText(Path.Combine(_sourceDir, "changed.txt"), "new content");

        // Pre-fill target with identical unchanged file
        var unchangedTarget = Path.Combine(_targetDir, "unchanged.txt");
        File.WriteAllText(unchangedTarget, "same");
        File.SetLastWriteTimeUtc(unchangedTarget,
            File.GetLastWriteTimeUtc(Path.Combine(_sourceDir, "unchanged.txt")));

        SeedJob("diff-test", BackupType.Differential);
        var manager = CreateManager(new FullBackupStrategy(), new DifferentialBackupStrategy());

        manager.ExecuteJob("diff-test");

        // changed.txt should be copied
        Assert.True(File.Exists(Path.Combine(_targetDir, "changed.txt")));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(_targetDir, "changed.txt")));
    }

    [Fact]
    public void ExecuteJob_ErrorOnOneFile_ContinuesAndLogsNegativeTime()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "good.txt"), "ok");
        File.WriteAllText(Path.Combine(_sourceDir, "bad.txt"), "will fail");

        // Lock bad.txt target path by making it a read-only directory (causes copy to fail)
        var badTargetDir = Path.Combine(_targetDir, "bad.txt");
        Directory.CreateDirectory(badTargetDir);

        SeedJob("error-test", BackupType.Full);
        var manager = CreateManager(new FullBackupStrategy(), new DifferentialBackupStrategy());

        manager.ExecuteJob("error-test");

        // good.txt should still be copied
        Assert.True(File.Exists(Path.Combine(_targetDir, "good.txt")));

        // Check log has a negative transfer time for the failed file
        var logFiles = Directory.GetFiles(_logDir, "*.json");
        Assert.Single(logFiles);
        var logContent = File.ReadAllText(logFiles[0]);
        Assert.Contains("-1", logContent);
    }

    [Fact]
    public void ExecuteJob_UpdatesStateAtStartAndEnd()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "file.txt"), "data");

        SeedJob("state-test", BackupType.Full);
        var manager = CreateManager(new FullBackupStrategy(), new DifferentialBackupStrategy());

        manager.ExecuteJob("state-test");

        // After execution, state should be Inactive (job finished)
        var stateFile = Path.Combine(_dataDir, "state.json");
        Assert.True(File.Exists(stateFile));
        var stateJson = File.ReadAllText(stateFile);
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<StateEntry>>(stateJson);
        Assert.NotNull(entries);
        var entry = Assert.Single(entries);
        Assert.Equal(JobState.Inactive, entry.State);
        Assert.Equal(0, entry.FilesRemaining);
    }

    [Fact]
    public void ExecuteJob_LoggerThrows_StateStillFlipsToInactive()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "file.txt"), "data");

        SeedJob("logger-fail", BackupType.Full);
        var failingLogger = new ThrowingLogger();
        var manager = new BackupManager(
            failingLogger,
            new FullBackupStrategy(),
            new DifferentialBackupStrategy(),
            StateTracker.Instance,
            JobRepository.Instance);

        Assert.Throws<IOException>(() => manager.ExecuteJob("logger-fail"));

        var stateFile = Path.Combine(_dataDir, "state.json");
        Assert.True(File.Exists(stateFile));
        var stateJson = File.ReadAllText(stateFile);
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<StateEntry>>(stateJson);
        Assert.NotNull(entries);
        var entry = Assert.Single(entries);
        Assert.Equal(JobState.Inactive, entry.State);
    }

    private sealed class ThrowingLogger : IDailyLogger
    {
        public void Append(LogEntry entry) => throw new IOException("simulated logger failure");
    }
}
