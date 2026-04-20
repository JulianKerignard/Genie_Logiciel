using System.Text.Json;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

// Each test gets its own temp directory and reloads AppConfig to point JobRepository
// at an isolated jobs.json.
[Collection("StateCollection")]
public class JobRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _jobsFilePath;

    public JobRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easysave-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _jobsFilePath = Path.Combine(_tempDir, "jobs.json");

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { JobsFilePath = _jobsFilePath };
        File.WriteAllText(configPath, JsonSerializer.Serialize(payload));

        // JobRepository reads AppConfig.Instance.JobsFilePath on every call,
        // so reloading AppConfig here redirects I/O to this test's temp dir
        // without needing to reset the JobRepository singleton itself.
        AppConfig.Load(configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.False(File.Exists(_jobsFilePath));

        var jobs = JobRepository.Instance.Load();

        Assert.Empty(jobs);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip()
    {
        var original = new List<BackupJob>
        {
            new() { Name = "Daily", SourcePath = @"\\srv\\source", TargetPath = @"\\srv\\backup", Type = BackupType.Full },
            new() { Name = "Weekly", SourcePath = @"\\srv\\docs", TargetPath = @"\\srv\\archive", Type = BackupType.Differential }
        };

        JobRepository.Instance.Save(original);
        var reloaded = JobRepository.Instance.Load();

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("Daily", reloaded[0].Name);
        Assert.Equal(BackupType.Full, reloaded[0].Type);
        Assert.Equal("Weekly", reloaded[1].Name);
        Assert.Equal(BackupType.Differential, reloaded[1].Type);
    }

    [Fact]
    public void Save_Atomic_NoPartialWrite()
    {
        var jobs = new List<BackupJob>
        {
            new() { Name = "A", SourcePath = "s1", TargetPath = "t1" },
            new() { Name = "B", SourcePath = "s2", TargetPath = "t2" }
        };

        for (var i = 0; i < 5; i++)
        {
            JobRepository.Instance.Save(jobs);

            var content = File.ReadAllText(_jobsFilePath);
            var parsed = JsonSerializer.Deserialize<List<BackupJob>>(content);
            Assert.NotNull(parsed);
            Assert.Equal(2, parsed!.Count);

            Assert.False(File.Exists(_jobsFilePath + ".tmp"));
        }
    }
}
