using System.Text.Json;
using EasyLog;

namespace EasySave.Tests.V2;

public class LogEntryV2Tests
{
    [Fact]
    public void EncryptionTimeMs_DefaultsToNull()
    {
        var entry = new LogEntry();

        Assert.Null(entry.EncryptionTimeMs);
    }

    [Fact]
    public void Serialize_NullEncryptionTime_OmitsProperty()
    {
        // v1 consumers reading the file must see the v1 shape exactly,
        // so EncryptionTimeMs must not appear when it is null.
        var entry = new LogEntry { JobName = "v1-shape" };

        var json = JsonSerializer.Serialize(entry);

        Assert.DoesNotContain("EncryptionTimeMs", json);
    }

    [Fact]
    public void Serialize_NonNullEncryptionTime_IncludesProperty()
    {
        var entry = new LogEntry { JobName = "encrypted", EncryptionTimeMs = 42 };

        var json = JsonSerializer.Serialize(entry);

        Assert.Contains("\"EncryptionTimeMs\":42", json);
    }

    [Fact]
    public void Roundtrip_PreservesEncryptionTime()
    {
        var entry = new LogEntry { JobName = "roundtrip", EncryptionTimeMs = -1 };

        var json = JsonSerializer.Serialize(entry);
        var roundtripped = JsonSerializer.Deserialize<LogEntry>(json);

        Assert.NotNull(roundtripped);
        Assert.Equal(-1, roundtripped!.EncryptionTimeMs);
    }

    [Fact]
    public void Deserialize_V1Json_LeavesEncryptionTimeNull()
    {
        // A v1 file (written before EncryptionTimeMs existed) must deserialize
        // cleanly with the new property left at null.
        const string v1Json = """
        {
          "Timestamp": "2026-04-22T13:35:46.7763460+02:00",
          "JobName": "v1-job",
          "SourceFile": "\\\\?\\C:\\src\\a.txt",
          "TargetFile": "\\\\?\\C:\\dst\\a.txt",
          "FileSize": 38,
          "FileTransferTimeMs": 0
        }
        """;

        var entry = JsonSerializer.Deserialize<LogEntry>(v1Json);

        Assert.NotNull(entry);
        Assert.Equal("v1-job", entry!.JobName);
        Assert.Null(entry.EncryptionTimeMs);
    }
}
