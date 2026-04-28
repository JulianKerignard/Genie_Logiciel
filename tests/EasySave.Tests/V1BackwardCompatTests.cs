using System.Text.Json;
using EasyLog;
using EasySave;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

// Verifies V2 can still read the files written by a V1.0 deployment after upgrade,
// covering the four artefacts the V1 user keeps on disk:
// appsettings.json, jobs.json, state.json, and per-day Logs/*.json files.
[Collection("StateCollection")]
public class V1BackwardCompatTests : IDisposable
{
    private readonly string _tempDir;

    public V1BackwardCompatTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "v1-compat-" + Guid.NewGuid().ToString("N"));
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
    public void AppConfig_Load_AcceptsV1AppSettingsJson_WithCapitalLanguageKey()
    {
        // Real V1.0.0 appsettings.json shape: only one key, capital L.
        var v1File = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(v1File, """{"Language": "fr"}""");

        AppConfig.Load(v1File);

        Assert.Equal("fr", AppConfig.Instance.Settings.Language);
        // Other V2 settings stay on their defaults (V1 file has no encrypted_extensions etc.).
        Assert.Empty(AppConfig.Instance.Settings.EncryptedExtensions);
        Assert.Empty(AppConfig.Instance.Settings.BusinessSoftware);
        Assert.Equal("json", AppConfig.Instance.Settings.LogFormat);
    }

    [Fact]
    public void JobRepository_Load_ReadsV1JobsJson()
    {
        // V1 wrote BackupJob with default System.Text.Json PascalCase keys.
        var jobsFile = Path.Combine(_tempDir, "jobs.json");
        File.WriteAllText(jobsFile, """
        [
          {
            "Name": "Photos",
            "SourcePath": "C:\\Users\\Alice\\Pictures",
            "TargetPath": "D:\\Backups\\Photos",
            "Type": 0
          },
          {
            "Name": "Docs",
            "SourcePath": "C:\\Users\\Alice\\Documents",
            "TargetPath": "D:\\Backups\\Docs",
            "Type": 1
          }
        ]
        """);

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { JobsFilePath = jobsFile }));
        AppConfig.Load(configPath);

        var jobs = JobRepository.Instance.Load();

        Assert.Equal(2, jobs.Count);
        Assert.Equal("Photos", jobs[0].Name);
        Assert.Equal(BackupType.Full, jobs[0].Type);
        Assert.Equal("Docs", jobs[1].Name);
        Assert.Equal(BackupType.Differential, jobs[1].Type);
    }

    [Fact]
    public void StateTracker_Reads_V1StateJson_OnNextUpdate()
    {
        // V1 state.json shape: PascalCase keys. The new entry must be added without
        // dropping the existing V1 entries.
        var stateFile = Path.Combine(_tempDir, "state.json");
        File.WriteAllText(stateFile, """
        [
          {
            "Name": "OldJobV1",
            "LastActionTime": "2025-12-15T10:00:00+01:00",
            "State": 0,
            "TotalFilesEligible": 100,
            "TotalSize": 1024,
            "FilesRemaining": 0,
            "SizeRemaining": 0,
            "CurrentSource": "",
            "CurrentTarget": ""
          }
        ]
        """);

        var configPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { StateFilePath = stateFile }));
        AppConfig.Load(configPath);

        StateTracker.Instance.Update(new StateEntry
        {
            Name = "NewJobV2",
            State = JobState.Active
        });

        var states = JsonSerializer.Deserialize<List<StateEntry>>(File.ReadAllText(stateFile))!;
        Assert.Equal(2, states.Count);
        Assert.Contains(states, s => s.Name == "OldJobV1");
        Assert.Contains(states, s => s.Name == "NewJobV2");
    }

    [Fact]
    public void LogEntry_Deserializes_V1Log_WithoutEncryptionField()
    {
        // V1 daily JSON logs had no EncryptionTimeMs field. V2 must still parse them.
        var v1Log = """
        [
          {
            "Timestamp": "2025-12-15T10:00:00",
            "JobName": "Photos",
            "SourceFile": "\\\\share\\src\\a.txt",
            "TargetFile": "\\\\share\\dst\\a.txt",
            "FileSize": 42,
            "FileTransferTimeMs": 12
          }
        ]
        """;

        var entries = JsonSerializer.Deserialize<List<LogEntry>>(v1Log)!;

        Assert.Single(entries);
        Assert.Equal("Photos", entries[0].JobName);
        Assert.Equal(42, entries[0].FileSize);
        Assert.Equal(12, entries[0].FileTransferTimeMs);
        Assert.Null(entries[0].EncryptionTimeMs);
    }

    [Fact]
    public void LogEntry_V1Roundtrip_StaysCompatible()
    {
        // Writing a V1-shaped LogEntry (no encryption) must produce a payload V1 consumers
        // can still parse — i.e. the EncryptionTimeMs key must be absent when null.
        var entry = new LogEntry
        {
            Timestamp = "2025-12-15T10:00:00",
            JobName = "Photos",
            SourceFile = @"\\share\src\a.txt",
            TargetFile = @"\\share\dst\a.txt",
            FileSize = 42,
            FileTransferTimeMs = 12,
            EncryptionTimeMs = null,
        };

        var json = JsonSerializer.Serialize(entry);

        Assert.DoesNotContain("EncryptionTimeMs", json);
    }
}
