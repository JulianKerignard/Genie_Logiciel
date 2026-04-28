using System.Text.Json;
using EasyLog;

namespace EasySave.Tests.V2;

public class JsonDailyLoggerEncryptionTests : IDisposable
{
    private readonly string _tempDir;

    public JsonDailyLoggerEncryptionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easylog-v2-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Append_PreservesEncryptionTimeMs_WhenSet()
    {
        // Regression: the v1 logger reconstructs the entry to normalize paths.
        // EncryptionTimeMs must propagate through the copy, otherwise v2 callers
        // would silently lose the encryption duration in the daily log file.
        var logger = new JsonDailyLogger(_tempDir);

        logger.Append(new LogEntry
        {
            Timestamp = "2026-04-28T10:00:00+02:00",
            JobName = "encrypt-job",
            SourceFile = @"\\nas\share\src.docx",
            TargetFile = @"\\nas\share\dst.docx",
            FileSize = 1024,
            FileTransferTimeMs = 5,
            EncryptionTimeMs = 17,
        });

        var dailyFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        var entries = JsonSerializer.Deserialize<List<LogEntry>>(File.ReadAllText(dailyFile));

        Assert.NotNull(entries);
        var saved = Assert.Single(entries!);
        Assert.Equal(17, saved.EncryptionTimeMs);
    }

    [Fact]
    public void Append_NoEncryption_KeepsV1FileShape()
    {
        // A v2 caller that does not set EncryptionTimeMs must produce a daily file
        // byte-shape-compatible with v1 (no extra property in the JSON).
        var logger = new JsonDailyLogger(_tempDir);

        logger.Append(new LogEntry
        {
            Timestamp = "2026-04-28T10:00:00+02:00",
            JobName = "plain-job",
            SourceFile = @"\\nas\share\src.txt",
            TargetFile = @"\\nas\share\dst.txt",
            FileSize = 256,
            FileTransferTimeMs = 1,
            // EncryptionTimeMs intentionally left null
        });

        var dailyFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        var raw = File.ReadAllText(dailyFile);

        Assert.DoesNotContain("EncryptionTimeMs", raw);
    }
}
