using System.Text.Json;
using EasySave;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

// Issue #69: a transient IOException at read time used to be swallowed and downgraded to an empty
// list, causing the next Save() to wipe every previously-saved entry. The fix is to let the
// IOException propagate so the caller aborts the destructive write path.
//
// Tests are Windows-only because Unix file locks (flock / FileShare.None on .NET) are advisory
// and do not actually block File.ReadAllText. We still ship them so Windows CI / dev boxes
// regression-check the fix.
[Collection("StateCollection")]
public class PersistenceLockPropagationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _jobsFilePath;
    private readonly string _stateFilePath;

    public PersistenceLockPropagationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "persist-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _jobsFilePath = Path.Combine(_tempDir, "jobs.json");
        _stateFilePath = Path.Combine(_tempDir, "state.json");

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        var payload = new { JobsFilePath = _jobsFilePath, StateFilePath = _stateFilePath };
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
    public void JobRepository_Load_PropagatesIOException_WhenFileLocked()
    {
        Skip.IfNot(OperatingSystem.IsWindows(),
            "Unix file locks are advisory; File.ReadAllText is not blocked by FileShare.None.");

        var existing = new[]
        {
            new BackupJob { Name = "Existing1", SourcePath = @"\\share\src", TargetPath = @"\\share\tgt", Type = BackupType.Full }
        };
        File.WriteAllText(_jobsFilePath, JsonSerializer.Serialize(existing));

        using var lockHolder = new FileStream(_jobsFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() => JobRepository.Instance.Load());
    }

    [SkippableFact]
    public void JobRepository_Load_PreservesFile_WhenIOExceptionPropagates()
    {
        Skip.IfNot(OperatingSystem.IsWindows(),
            "Unix file locks are advisory; File.ReadAllText is not blocked by FileShare.None.");

        var existing = new[]
        {
            new BackupJob { Name = "A", SourcePath = @"\\share\a", TargetPath = @"\\share\at", Type = BackupType.Full },
            new BackupJob { Name = "B", SourcePath = @"\\share\b", TargetPath = @"\\share\bt", Type = BackupType.Differential },
        };
        var originalJson = JsonSerializer.Serialize(existing);
        File.WriteAllText(_jobsFilePath, originalJson);

        using (var lockHolder = new FileStream(_jobsFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => JobRepository.Instance.Load());
        }

        Assert.Equal(originalJson, File.ReadAllText(_jobsFilePath));
    }

    [SkippableFact]
    public void StateTracker_Update_PropagatesIOException_WhenFileLocked()
    {
        Skip.IfNot(OperatingSystem.IsWindows(),
            "Unix file locks are advisory; File.ReadAllText is not blocked by FileShare.None.");

        File.WriteAllText(_stateFilePath, "[]");
        using var lockHolder = new FileStream(_stateFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        var entry = new StateEntry { Name = "Foo", State = JobState.Active };
        Assert.Throws<IOException>(() => StateTracker.Instance.Update(entry));
    }

    [SkippableFact]
    public void StateTracker_Update_PreservesFile_WhenIOExceptionPropagates()
    {
        Skip.IfNot(OperatingSystem.IsWindows(),
            "Unix file locks are advisory; File.ReadAllText is not blocked by FileShare.None.");

        var seeded = new[]
        {
            new StateEntry { Name = "OtherJob", State = JobState.Active, FilesRemaining = 3 }
        };
        var originalJson = JsonSerializer.Serialize(seeded);
        File.WriteAllText(_stateFilePath, originalJson);

        using (var lockHolder = new FileStream(_stateFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() =>
                StateTracker.Instance.Update(new StateEntry { Name = "Intruder", State = JobState.Active }));
        }

        Assert.Equal(originalJson, File.ReadAllText(_stateFilePath));
    }
}
