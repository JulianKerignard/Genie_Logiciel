using System.Text.Json;
using EasySave.Services;
using EasySave.UI.Models;
using EasySave.UI.Services;

namespace EasySave.Tests.V2;

// Issue #111: SchedulerService.GetAll() used to swallow IOException and return empty,
// which would let the next SaveAll() write the empty list over schedules.json and
// silently wipe the user's schedules — same trap as #69 / #97. The fix lets the
// IOException propagate so the caller (ScheduleViewModel / SchedulerDispatchService)
// aborts instead of overwriting.
//
// Windows-only because Unix file locks are advisory; File.ReadAllText is not blocked
// by FileShare.None on macOS / Linux.
[Collection("AppConfigMutation")]
public class SchedulerServiceLockPropagationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _schedulesFilePath;

    public SchedulerServiceLockPropagationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "scheduler-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _schedulesFilePath = Path.Combine(_tempDir, "schedules.json");

        // SchedulerService derives its path from the directory of AppConfig.JobsFilePath,
        // so we point JobsFilePath at the same temp directory.
        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { JobsFilePath = Path.Combine(_tempDir, "jobs.json") };
        File.WriteAllText(configPath, JsonSerializer.Serialize(payload));
        AppConfig.Load(configPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [SkippableFact]
    public void GetAll_PropagatesIOException_WhenFileLocked()
    {
        Skip.IfNot(OperatingSystem.IsWindows(),
            "Unix file locks are advisory; File.ReadAllText is not blocked by FileShare.None.");

        var existing = new[]
        {
            new ScheduledJob { JobName = "Photos", IsEnabled = true, IntervalMinutes = 60 }
        };
        File.WriteAllText(_schedulesFilePath, JsonSerializer.Serialize(existing));

        using var lockHolder = new FileStream(_schedulesFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        var service = new SchedulerService();
        Assert.Throws<IOException>(() => service.GetAll());
    }

    [SkippableFact]
    public void GetAll_PreservesFile_WhenIOExceptionPropagates()
    {
        Skip.IfNot(OperatingSystem.IsWindows(),
            "Unix file locks are advisory; File.ReadAllText is not blocked by FileShare.None.");

        var existing = new[]
        {
            new ScheduledJob { JobName = "Photos", IsEnabled = true, IntervalMinutes = 60 },
            new ScheduledJob { JobName = "Docs", IsEnabled = false, IntervalMinutes = 30 },
        };
        var originalJson = JsonSerializer.Serialize(existing);
        File.WriteAllText(_schedulesFilePath, originalJson);

        var service = new SchedulerService();
        using (var lockHolder = new FileStream(_schedulesFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => service.GetAll());
        }

        Assert.Equal(originalJson, File.ReadAllText(_schedulesFilePath));
    }

    [Fact]
    public void GetAll_QuarantinesCorruptedFile_AndReturnsEmpty()
    {
        File.WriteAllText(_schedulesFilePath, "{ not valid json");

        var service = new SchedulerService();
        var result = service.GetAll();

        Assert.Empty(result);
        Assert.NotEmpty(Directory.GetFiles(_tempDir, "schedules.json.corrupted-*"));
    }
}
