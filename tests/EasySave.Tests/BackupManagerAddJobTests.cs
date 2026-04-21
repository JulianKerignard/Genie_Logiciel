using EasyLog;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class BackupManagerAddJobTests : IDisposable
{
    private readonly string _tempDir;

    public BackupManagerAddJobTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bm-addjob-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            LogDirectory = Path.Combine(_tempDir, "logs"),
            StateFilePath = Path.Combine(_tempDir, "state.json"),
            JobsFilePath = Path.Combine(_tempDir, "jobs.json"),
        }));
        AppConfig.Load(configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BackupManager CreateManager()
    {
        var logger = new JsonDailyLogger(Path.Combine(_tempDir, "logs"));
        return new BackupManager(
            logger,
            new FullBackupStrategy(),
            new DifferentialBackupStrategy(),
            StateTracker.Instance,
            JobRepository.Instance);
    }

    private static BackupJob MakeJob(string name) => new()
    {
        Name = name,
        SourcePath = "/tmp/src",
        TargetPath = "/tmp/dst",
        Type = BackupType.Full,
    };

    [Fact]
    public void AddJob_WhenLessThan5_Succeeds()
    {
        var manager = CreateManager();

        for (int i = 1; i <= 5; i++)
            manager.AddJob(MakeJob($"job-{i}"));

        Assert.Equal(5, manager.ListJobs().Count);
    }

    [Fact]
    public void AddJob_When5Already_Throws()
    {
        var manager = CreateManager();

        for (int i = 1; i <= 5; i++)
            manager.AddJob(MakeJob($"job-{i}"));

        var ex = Assert.Throws<InvalidOperationException>(() => manager.AddJob(MakeJob("job-6")));
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void AddJob_DuplicateName_Throws()
    {
        var manager = CreateManager();
        manager.AddJob(MakeJob("backup-daily"));

        var ex = Assert.Throws<InvalidOperationException>(() => manager.AddJob(MakeJob("backup-daily")));
        Assert.Contains("backup-daily", ex.Message);
    }

    [Fact]
    public void AddJob_PersistsAcrossInstances()
    {
        // Clear any leftover jobs from other tests
        JobRepository.Instance.Save(new List<BackupJob>());

        var manager = CreateManager();
        manager.AddJob(MakeJob("persisted-job"));

        var freshManager = CreateManager();
        var jobs = freshManager.ListJobs();

        Assert.Single(jobs);
        Assert.Equal("persisted-job", jobs[0].Name);
    }

    [Theory]
    [InlineData("", "/src", "/dst")]
    [InlineData("   ", "/src", "/dst")]
    [InlineData("job", "", "/dst")]
    [InlineData("job", "/src", "")]
    public void AddJob_EmptyField_Throws(string name, string source, string target)
    {
        JobRepository.Instance.Save(new List<BackupJob>());
        var manager = CreateManager();

        var job = new BackupJob { Name = name, SourcePath = source, TargetPath = target, Type = BackupType.Full };

        Assert.Throws<ArgumentException>(() => manager.AddJob(job));
        Assert.Empty(manager.ListJobs());
    }
}
