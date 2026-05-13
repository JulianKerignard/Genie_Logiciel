using EasyLog;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests.V2;

// Smoke tests confirming the V2 test project is wired up against EasyLog and EasySave.
// Real V2 test classes (V2 logger formatters, encryption time, restore, scheduler...) will
// land here once the corresponding features are implemented.
[Collection("AppConfigMutation")]
public class SmokeTests
{
    [Fact]
    public void EasyLog_LogEntry_IsAccessible()
    {
        var entry = new LogEntry
        {
            Timestamp = "2026-04-27T00:00:00+02:00",
            JobName = "smoke",
            SourceFile = "/tmp/source.txt",
            TargetFile = "/tmp/target.txt",
            FileSize = 42,
            FileTransferTimeMs = 1,
        };

        Assert.Equal("smoke", entry.JobName);
    }

    [Fact]
    public void EasySave_BackupJob_IsAccessible()
    {
        var job = new BackupJob
        {
            Name = "smoke",
            SourcePath = "/tmp/src",
            TargetPath = "/tmp/dst",
            Type = BackupType.Full,
        };

        Assert.Equal(BackupType.Full, job.Type);
    }

    [Fact]
    public void EasySave_AppConfig_HasDefaults()
    {
        Assert.NotNull(AppConfig.Instance);
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.Instance.LogDirectory));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.Instance.StateFilePath));
        Assert.False(string.IsNullOrWhiteSpace(AppConfig.Instance.JobsFilePath));
    }
}
