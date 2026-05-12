using System.Text.Json;
using System.Xml.Linq;
using EasyLog;

namespace EasySave.Tests.V2;

/// <summary>
/// Verifies EasyLog 1.2.0 host stamping: MachineName / UserName are
/// optional, auto-populated from <see cref="Environment"/> when the
/// caller leaves them null, preserved across the logger's path
/// normalization step, and omitted from the JSON / XML output when null
/// so v1 / v2 readers stay compatible byte-for-byte.
/// </summary>
public class LogEntryHostFieldsTests : IDisposable
{
    private readonly string _tempDir;

    public LogEntryHostFieldsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easylog-hostfields-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Deserialize_OldV1V2Json_WithoutHostFields_StillWorks()
    {
        // A pre-1.2 daily file (no MachineName / UserName elements) must
        // continue to deserialize cleanly into the v1.2 LogEntry POCO.
        const string v1Json = """
            [
              {
                "Timestamp": "2026-01-15T10:00:00+01:00",
                "JobName": "legacy",
                "SourceFile": "\\\\nas\\share\\a.txt",
                "TargetFile": "\\\\nas\\share\\b.txt",
                "FileSize": 1024,
                "FileTransferTimeMs": 12
              }
            ]
            """;

        var entries = JsonSerializer.Deserialize<List<LogEntry>>(v1Json);

        Assert.NotNull(entries);
        Assert.Single(entries!);
        Assert.Equal("legacy", entries![0].JobName);
        // Default null on a pre-1.2 row — readers must treat the fields
        // as optional and never assume they are populated.
        Assert.Null(entries[0].MachineName);
        Assert.Null(entries[0].UserName);
    }

    [Fact]
    public void Serialize_LogEntry_WithoutHostFields_OmitsThemFromJson()
    {
        // Regression: a v1.x caller that does not set the new fields must
        // see the exact same JSON shape as before — no empty "MachineName":
        // null pollution in the daily file.
        var entry = new LogEntry
        {
            Timestamp = "2026-05-12T10:00:00+02:00",
            JobName = "v1-caller",
            SourceFile = "src",
            TargetFile = "dst",
            FileSize = 1,
            FileTransferTimeMs = 1,
        };

        string json = JsonSerializer.Serialize(entry);

        Assert.DoesNotContain("MachineName", json);
        Assert.DoesNotContain("UserName", json);
    }

    [Fact]
    public void JsonDailyLogger_AutoStamps_HostFields_WhenCallerLeavesThemNull()
    {
        using (var logger = new JsonDailyLogger(_tempDir))
        {
            logger.Append(new LogEntry
            {
                Timestamp = "2026-05-12T10:00:00+02:00",
                JobName = "stamp-me",
                SourceFile = "src",
                TargetFile = "dst",
                FileSize = 1,
                FileTransferTimeMs = 1,
            });
        }

        var file = Directory.GetFiles(_tempDir, "*.json").Single();
        var entries = JsonSerializer.Deserialize<List<LogEntry>>(File.ReadAllText(file))!;

        Assert.Equal(Environment.MachineName, entries[0].MachineName);
        Assert.Equal(Environment.UserName, entries[0].UserName);
    }

    [Fact]
    public void JsonDailyLogger_PreservesCallerProvidedHostFields_AcrossNormalization()
    {
        // A central collector receiving an entry from a remote host writes
        // it to its own daily file. The original sender's MachineName / UserName
        // must survive normalization — the collector must not overwrite them
        // with its own Environment values.
        using (var logger = new JsonDailyLogger(_tempDir))
        {
            logger.Append(new LogEntry
            {
                Timestamp = "2026-05-12T10:00:00+02:00",
                JobName = "from-remote",
                SourceFile = "src",
                TargetFile = "dst",
                FileSize = 1,
                FileTransferTimeMs = 1,
                MachineName = "WS-OPERATOR-07",
                UserName = "alice",
            });
        }

        var file = Directory.GetFiles(_tempDir, "*.json").Single();
        var entries = JsonSerializer.Deserialize<List<LogEntry>>(File.ReadAllText(file))!;

        Assert.Equal("WS-OPERATOR-07", entries[0].MachineName);
        Assert.Equal("alice", entries[0].UserName);
    }

    [Fact]
    public void XmlDailyLogger_AutoStamps_HostFields_WhenCallerLeavesThemNull()
    {
        var logger = new XmlDailyLogger(_tempDir);
        logger.Append(new LogEntry
        {
            Timestamp = "2026-05-12T10:00:00+02:00",
            JobName = "stamp-me-xml",
            SourceFile = "src",
            TargetFile = "dst",
            FileSize = 1,
            FileTransferTimeMs = 1,
        });

        var file = Directory.GetFiles(_tempDir, "*.xml").Single();
        var doc = XDocument.Load(file);
        var entry = doc.Root!.Element("Entry")!;

        Assert.Equal(Environment.MachineName, entry.Element("MachineName")?.Value);
        Assert.Equal(Environment.UserName, entry.Element("UserName")?.Value);
    }

    [Fact]
    public void XmlFormatter_OmitsHostElements_WhenFieldsAreNull()
    {
        // Regression: a v1 / v2 entry without host fields must produce the
        // exact element set v1 / v2 readers expect — no empty <MachineName/>
        // sneaking into the file.
        var formatter = new XmlFormatter();
        var entry = new LogEntry
        {
            Timestamp = "2026-05-12T10:00:00+02:00",
            JobName = "v1-row",
            SourceFile = "src",
            TargetFile = "dst",
            FileSize = 1,
            FileTransferTimeMs = 1,
        };

        string xml = formatter.Format(entry);

        Assert.DoesNotContain("<MachineName", xml);
        Assert.DoesNotContain("<UserName", xml);
    }

    [Fact]
    public void XmlFormatter_EmitsHostElements_WhenFieldsAreSet()
    {
        var formatter = new XmlFormatter();
        var entry = new LogEntry
        {
            Timestamp = "2026-05-12T10:00:00+02:00",
            JobName = "v1.2-row",
            SourceFile = "src",
            TargetFile = "dst",
            FileSize = 1,
            FileTransferTimeMs = 1,
            MachineName = "BUILD-VM-01",
            UserName = "ci",
        };

        var doc = XDocument.Parse(formatter.Format(entry));

        Assert.Equal("BUILD-VM-01", doc.Root!.Element("MachineName")?.Value);
        Assert.Equal("ci", doc.Root.Element("UserName")?.Value);
    }
}
