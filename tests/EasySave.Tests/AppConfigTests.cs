using System.Text.Json;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class AppConfigTests : IDisposable
{
    private readonly string _tempDir;

    public AppConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "appconfig-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_KeepsDefaults()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.json");

        AppConfig.Load(missing);

        Assert.NotNull(AppConfig.Instance);
        Assert.Equal("en", AppConfig.Instance.Language);
    }

    [Fact]
    public void Load_ValidFile_AppliesValues()
    {
        // Paths under _tempDir so the EnsurePathsWritable side-effect of
        // Load (creates the dirs if missing) only touches our cleanup-tracked
        // temp tree, not /tmp at large.
        var file = Path.Combine(_tempDir, "settings.json");
        var customLog = Path.Combine(_tempDir, "custom-logs");
        var customState = Path.Combine(_tempDir, "state-dir", "custom-state.json");
        var customJobs = Path.Combine(_tempDir, "jobs-dir", "custom-jobs.json");
        var payload = new
        {
            LogDirectory = customLog,
            StateFilePath = customState,
            JobsFilePath = customJobs,
            Language = "fr"
        };
        File.WriteAllText(file, JsonSerializer.Serialize(payload));

        AppConfig.Load(file);

        Assert.Equal(customLog, AppConfig.Instance.LogDirectory);
        Assert.Equal(customState, AppConfig.Instance.StateFilePath);
        Assert.Equal(customJobs, AppConfig.Instance.JobsFilePath);
        Assert.Equal("fr", AppConfig.Instance.Language);
    }

    [Fact]
    public void Load_NonexistentDirs_AreCreatedAtStartup()
    {
        // B5: the first JsonDailyLogger / StateTracker / JobRepository write
        // used to crash with DirectoryNotFoundException when the configured
        // directories had not been created yet. Load now ensures them so the
        // failure (if any) surfaces here, with a clear path in the message.
        var file = Path.Combine(_tempDir, "settings.json");
        var logDir = Path.Combine(_tempDir, "deep", "nested", "logs");
        var stateDir = Path.Combine(_tempDir, "deep", "state");
        var jobsDir = Path.Combine(_tempDir, "deep", "jobs");
        var payload = new
        {
            LogDirectory = logDir,
            StateFilePath = Path.Combine(stateDir, "state.json"),
            JobsFilePath = Path.Combine(jobsDir, "jobs.json"),
        };
        File.WriteAllText(file, JsonSerializer.Serialize(payload));

        AppConfig.Load(file);

        Assert.True(Directory.Exists(logDir), "LogDirectory should be created.");
        Assert.True(Directory.Exists(stateDir), "StateFilePath parent should be created.");
        Assert.True(Directory.Exists(jobsDir), "JobsFilePath parent should be created.");
    }

    [Fact]
    public void Load_UnwritablePath_DoesNotThrow()
    {
        // Same B5: an invalid / unwritable path must not crash the startup.
        // The actual error surfaces later at the write site, but Load itself
        // is best-effort and only logs to stderr.
        var file = Path.Combine(_tempDir, "settings.json");
        // Embedded null byte makes the path unconditionally invalid on
        // every platform — Directory.CreateDirectory throws ArgumentException.
        var bogus = Path.Combine(_tempDir, "bad\0path");
        var payload = new
        {
            LogDirectory = bogus,
            StateFilePath = Path.Combine(bogus, "state.json"),
            JobsFilePath = Path.Combine(bogus, "jobs.json"),
        };
        File.WriteAllText(file, JsonSerializer.Serialize(payload));

        var ex = Record.Exception(() => AppConfig.Load(file));
        Assert.Null(ex);
    }

    [Fact]
    public void Load_CorruptedFile_FallsBackToDefaults()
    {
        var file = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(file, "{ not valid json");

        AppConfig.Load(file);

        Assert.Equal("en", AppConfig.Instance.Language);
    }
}
