using System.Text.Json;
using EasyLog;

namespace EasySave.Tests.V2;

public class JsonFormatterTests
{
    [Fact]
    public void FileExtension_IsJson()
    {
        var formatter = new JsonFormatter();
        Assert.Equal(".json", formatter.FileExtension);
    }

    [Fact]
    public void Format_NullEntry_Throws()
    {
        var formatter = new JsonFormatter();
        Assert.Throws<ArgumentNullException>(() => formatter.Format(null!));
    }

    [Fact]
    public void Format_OmitsEncryptionTimeMs_WhenNull()
    {
        // V1 byte-shape compatibility: a non-encrypted entry must not surface
        // the v2-only field, so v1 readers see exactly the v1 file shape.
        var formatter = new JsonFormatter();
        var entry = new LogEntry { JobName = "v1-shape" };

        var json = formatter.Format(entry);

        Assert.DoesNotContain("EncryptionTimeMs", json);
    }

    [Fact]
    public void Format_IncludesEncryptionTimeMs_WhenSet()
    {
        var formatter = new JsonFormatter();
        var entry = new LogEntry { JobName = "encrypted", EncryptionTimeMs = 42 };

        var roundtripped = JsonSerializer.Deserialize<LogEntry>(formatter.Format(entry));

        Assert.NotNull(roundtripped);
        Assert.Equal(42, roundtripped!.EncryptionTimeMs);
    }

    [Fact]
    public void Format_RoundTrip_PreservesAllFields()
    {
        var formatter = new JsonFormatter();
        var original = new LogEntry
        {
            Timestamp = "2026-04-29T08:00:00+02:00",
            JobName = "roundtrip",
            SourceFile = @"\\nas\share\src.docx",
            TargetFile = @"\\nas\share\dst.docx",
            FileSize = 1024,
            FileTransferTimeMs = 5,
            EncryptionTimeMs = 17,
        };

        var rebuilt = JsonSerializer.Deserialize<LogEntry>(formatter.Format(original));

        Assert.NotNull(rebuilt);
        Assert.Equal(original.Timestamp, rebuilt!.Timestamp);
        Assert.Equal(original.JobName, rebuilt.JobName);
        Assert.Equal(original.SourceFile, rebuilt.SourceFile);
        Assert.Equal(original.TargetFile, rebuilt.TargetFile);
        Assert.Equal(original.FileSize, rebuilt.FileSize);
        Assert.Equal(original.FileTransferTimeMs, rebuilt.FileTransferTimeMs);
        Assert.Equal(original.EncryptionTimeMs, rebuilt.EncryptionTimeMs);
    }

    [Fact]
    public void RoundTrip_NegativeFileTransferTime_PreservedAsErrorSignal()
    {
        // Cahier: FileTransferTimeMs < 0 is the error signal for a failed copy.
        // The JSON form must preserve negative values exactly.
        var formatter = new JsonFormatter();
        var entry = new LogEntry { JobName = "copy-failed", FileTransferTimeMs = -1 };

        var rebuilt = JsonSerializer.Deserialize<LogEntry>(formatter.Format(entry));

        Assert.Equal(-1, rebuilt!.FileTransferTimeMs);
    }

    [Fact]
    public void Output_IsCompatibleWithV1Reader()
    {
        // A "v1 reader" is any consumer that expected the v1 LogEntry shape
        // (no EncryptionTimeMs field). Reading a v2 JSON object that omits
        // EncryptionTimeMs must still produce a clean entry.
        var formatter = new JsonFormatter();
        var v2EntryWithoutEncryption = new LogEntry
        {
            Timestamp = "2026-04-29T08:00:00+02:00",
            JobName = "no-encrypt",
            FileSize = 256,
            FileTransferTimeMs = 1,
        };

        var json = formatter.Format(v2EntryWithoutEncryption);
        var rebuilt = JsonSerializer.Deserialize<LogEntry>(json);

        Assert.DoesNotContain("EncryptionTimeMs", json);
        Assert.NotNull(rebuilt);
        Assert.Equal("no-encrypt", rebuilt!.JobName);
        Assert.Null(rebuilt.EncryptionTimeMs);
    }

    [Fact]
    public void Format_IsIndented()
    {
        // Daily log files are read by humans on the customer site (cahier:
        // "no extra tooling needed"). The hand-readable shape is part of the contract.
        var formatter = new JsonFormatter();
        var json = formatter.Format(new LogEntry { JobName = "indented" });

        Assert.Contains("\n", json);
    }
}
