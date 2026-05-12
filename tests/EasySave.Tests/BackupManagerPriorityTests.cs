using EasyLog;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class BackupManagerPriorityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly string _logDir;
    private readonly string _dataDir;

    public BackupManagerPriorityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bm-prio-tests-" + Guid.NewGuid().ToString("N"));
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

    private BackupManager CreateManager(
        IPriorityGate? priorityGate,
        IEnumerable<string> priorityExtensions,
        IDailyLogger? logger = null)
    {
        return new BackupManager(
            logger ?? new JsonDailyLogger(_logDir),
            new FullBackupStrategy(),
            new DifferentialBackupStrategy(),
            StateTracker.Instance,
            JobRepository.Instance,
            new NoOpEncryptionService(),
            Array.Empty<string>(),
            bigFileGate: null,
            priorityGate: priorityGate,
            priorityExtensions: priorityExtensions);
    }

    private void SeedJob(string name, string sourceDir)
    {
        JobRepository.Instance.Save(new List<BackupJob>
        {
            new() { Name = name, SourcePath = sourceDir, TargetPath = _targetDir, Type = BackupType.Full },
        });
    }

    // Captures the order of file-transfer log entries so the test can
    // assert that priority files were copied before non-priority files.
    private sealed class CapturingLogger : IDailyLogger
    {
        public List<LogEntry> Entries { get; } = new();
        public void Append(LogEntry entry)
        {
            lock (Entries) Entries.Add(entry);
        }
    }

    [Fact]
    public void ExecuteJob_PriorityFilesAreCopiedFirstWithinAJob()
    {
        // Source: 2 non-priority + 2 priority. Priorities must appear
        // first in the log, regardless of the ordinal file-name order.
        File.WriteAllText(Path.Combine(_sourceDir, "a-plain.txt"), "x");
        File.WriteAllText(Path.Combine(_sourceDir, "b-plain.txt"), "x");
        File.WriteAllText(Path.Combine(_sourceDir, "c-doc.docx"), "x");
        File.WriteAllText(Path.Combine(_sourceDir, "d-doc.docx"), "x");
        SeedJob("prio-order", _sourceDir);

        var logger = new CapturingLogger();
        var manager = CreateManager(
            priorityGate: null,
            priorityExtensions: new[] { ".docx" },
            logger: logger);

        manager.ExecuteJob("prio-order");

        var transferred = logger.Entries
            .Where(e => !string.IsNullOrEmpty(e.SourceFile))
            .Select(e => Path.GetFileName(e.SourceFile))
            .ToList();

        Assert.Equal(4, transferred.Count);
        // First two transferred must be the priorities, last two the plain files.
        Assert.All(transferred.Take(2),
            n => Assert.EndsWith(".docx", n, StringComparison.OrdinalIgnoreCase));
        Assert.All(transferred.Skip(2),
            n => Assert.EndsWith(".txt", n, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteJob_NonPriorityWaitsForOtherJobsPriorities()
    {
        // Two jobs, one shared PriorityGate. Job A has only one non-
        // priority file. Job B has one .docx (priority). Job A must
        // wait until B's .docx is done before copying its .txt.
        // Lay them out in two source dirs so each job sees its own
        // single file.
        var srcA = Path.Combine(_tempDir, "srcA");
        var srcB = Path.Combine(_tempDir, "srcB");
        Directory.CreateDirectory(srcA);
        Directory.CreateDirectory(srcB);
        File.WriteAllText(Path.Combine(srcA, "a.txt"), "x");
        File.WriteAllText(Path.Combine(srcB, "b.docx"), "x");

        JobRepository.Instance.Save(new List<BackupJob>
        {
            new() { Name = "JobA", SourcePath = srcA, TargetPath = Path.Combine(_targetDir, "A"), Type = BackupType.Full },
            new() { Name = "JobB", SourcePath = srcB, TargetPath = Path.Combine(_targetDir, "B"), Type = BackupType.Full },
        });

        using var gate = new PriorityGate();
        // Pre-register JobB with one priority before either ExecuteJob
        // call so JobA's WaitForNonPriorityWindow sees the pending
        // priority and parks. JobB.ExecuteJob will (re-)register and then
        // MarkPriorityFileDone after its .docx copy.
        gate.RegisterJob("JobB", priorityFileCount: 1);

        var loggerA = new CapturingLogger();
        var loggerB = new CapturingLogger();
        var managerA = CreateManager(gate, new[] { ".docx" }, loggerA);
        var managerB = CreateManager(gate, new[] { ".docx" }, loggerB);

        var aTask = Task.Run(() => managerA.ExecuteJob("JobA"));

        // A must still be parked on the gate (waiting for JobB to mark
        // its .docx done). 100 ms probe is generous on any CI runner.
        var winner = await Task.WhenAny(aTask, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(aTask, winner);
        Assert.Empty(loggerA.Entries.Where(e => !string.IsNullOrEmpty(e.SourceFile)));

        var bTask = Task.Run(() => managerB.ExecuteJob("JobB"));
        await bTask;

        // Once B is done, A's wait must clear and the .txt copies.
        await aTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(File.Exists(Path.Combine(_targetDir, "A", "a.txt")));
        Assert.True(File.Exists(Path.Combine(_targetDir, "B", "b.docx")));
    }
}
