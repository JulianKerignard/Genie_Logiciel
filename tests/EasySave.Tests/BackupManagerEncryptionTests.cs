using EasyLog;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.Tests;

[Collection("StateCollection")]
public class BackupManagerEncryptionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly string _logDir;
    private readonly string _dataDir;

    public BackupManagerEncryptionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bm-enc-tests-" + Guid.NewGuid().ToString("N"));
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

    private BackupManager CreateManager(IEncryptionService encryption, params string[] extensions)
    {
        var logger = new JsonDailyLogger(_logDir);
        return new BackupManager(
            logger,
            new FullBackupStrategy(),
            new DifferentialBackupStrategy(),
            StateTracker.Instance,
            JobRepository.Instance,
            encryption,
            extensions);
    }

    private void SeedJob(string name)
    {
        JobRepository.Instance.Save(new List<BackupJob>
        {
            new() { Name = name, SourcePath = _sourceDir, TargetPath = _targetDir, Type = BackupType.Full },
        });
    }

    private sealed class StubEncryption : IEncryptionService
    {
        private readonly EncryptResult _result;
        public List<(string Source, string Dest)> Calls { get; } = new();

        public StubEncryption(EncryptResult result) => _result = result;

        public EncryptResult Encrypt(string source, string dest)
        {
            Calls.Add((source, dest));
            // Simulate the real CryptoSoft side-effect on success: write the
            // (encrypted) target file so downstream assertions can find it.
            if (_result.Success)
            {
                File.WriteAllText(dest, $"encrypted({Path.GetFileName(source)})");
            }
            return _result;
        }
    }

    [Fact]
    public void Execute_EligibleExtension_RoutesToEncryption()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "report.pdf"), "secret");
        File.WriteAllText(Path.Combine(_sourceDir, "notes.txt"), "plain");
        SeedJob("crypto-job");

        var stub = new StubEncryption(EncryptResult.Succeeded(15));
        var manager = CreateManager(stub, ".pdf");

        manager.ExecuteJob("crypto-job");

        // PDF went through encryption, TXT through plain copy.
        Assert.Single(stub.Calls);
        Assert.EndsWith("report.pdf", stub.Calls[0].Source);
        Assert.Equal("encrypted(report.pdf)", File.ReadAllText(Path.Combine(_targetDir, "report.pdf")));
        Assert.Equal("plain", File.ReadAllText(Path.Combine(_targetDir, "notes.txt")));
    }

    [Fact]
    public void Execute_EncryptionFailure_LogsNegativeAndContinues()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "doomed.pdf"), "x");
        File.WriteAllText(Path.Combine(_sourceDir, "ok.txt"), "y");
        SeedJob("crypto-fail");

        var stub = new StubEncryption(EncryptResult.Failed(-7));
        var manager = CreateManager(stub, ".pdf");

        manager.ExecuteJob("crypto-fail");

        // Plain file still copied.
        Assert.True(File.Exists(Path.Combine(_targetDir, "ok.txt")));

        // Log has a negative EncryptionTimeMs for the failed file.
        var logFile = Directory.GetFiles(_logDir, "*.json").Single();
        var raw = File.ReadAllText(logFile);
        Assert.Contains("\"EncryptionTimeMs\": -7", raw);
    }

    [Fact]
    public void Execute_EmptyExtensionList_NoEncryptionCalled()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "report.pdf"), "secret");
        SeedJob("plain-job");

        var stub = new StubEncryption(EncryptResult.Succeeded(0));
        var manager = CreateManager(stub); // empty extensions

        manager.ExecuteJob("plain-job");

        Assert.Empty(stub.Calls);
        Assert.Equal("secret", File.ReadAllText(Path.Combine(_targetDir, "report.pdf")));
    }

    [Fact]
    public void Execute_ExtensionMatchIsCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "report.PDF"), "x");
        SeedJob("case-job");

        var stub = new StubEncryption(EncryptResult.Succeeded(0));
        var manager = CreateManager(stub, ".pdf");

        manager.ExecuteJob("case-job");

        Assert.Single(stub.Calls);
    }
}
