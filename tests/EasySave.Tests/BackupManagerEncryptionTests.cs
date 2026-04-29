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

        public bool IsAvailable => true;

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

    [Fact]
    public void Execute_DiffStrategy_EncryptedFileUnchanged_NotReEncrypted()
    {
        // v2.0 grille +1: differential backup must skip encrypted files when
        // the plaintext source is unchanged, even though the target file's
        // size never matches the source's.
        File.WriteAllText(Path.Combine(_sourceDir, "secret.pdf"), "secret");
        JobRepository.Instance.Save(new List<BackupJob>
        {
            new()
            {
                Name = "diff-crypto",
                SourcePath = _sourceDir,
                TargetPath = _targetDir,
                Type = BackupType.Differential,
            },
        });

        var stub = new StubEncryption(EncryptResult.Succeeded(5));
        var manager = CreateManager(stub, ".pdf");

        // First run: file is new, encryption fires.
        manager.ExecuteJob("diff-crypto");
        Assert.Single(stub.Calls);

        // Second run: nothing on disk changed, so DiffStrategy must skip the file.
        manager.ExecuteJob("diff-crypto");
        Assert.Single(stub.Calls); // still one call, not two
    }

    [Fact]
    public void Execute_DiffStrategy_EncryptedFileModified_ReEncrypted()
    {
        // v2.0 grille +1: when the plaintext source is modified after a first
        // diff backup, the second run must re-encrypt the file even though the
        // encrypted target's size is still different from the source's.
        var sourceFile = Path.Combine(_sourceDir, "secret.pdf");
        File.WriteAllText(sourceFile, "secret");
        JobRepository.Instance.Save(new List<BackupJob>
        {
            new()
            {
                Name = "diff-crypto-mod",
                SourcePath = _sourceDir,
                TargetPath = _targetDir,
                Type = BackupType.Differential,
            },
        });

        var stub = new StubEncryption(EncryptResult.Succeeded(5));
        var manager = CreateManager(stub, ".pdf");

        manager.ExecuteJob("diff-crypto-mod");
        Assert.Single(stub.Calls);

        // Touch the source: rewrite content and bump LastWriteTimeUtc clearly
        // forward so the strategy sees a newer source.
        File.WriteAllText(sourceFile, "secret v2");
        File.SetLastWriteTimeUtc(sourceFile, DateTime.UtcNow.AddMinutes(1));

        manager.ExecuteJob("diff-crypto-mod");
        Assert.Equal(2, stub.Calls.Count);
    }
}
