using System.Xml.Linq;
using System.Xml.Schema;
using EasyLog;

namespace EasySave.Tests.V2;

public class XmlDailyLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public XmlDailyLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easylog-xml-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string DailyFilePath() => Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.xml");

    [Fact]
    public void Constructor_NullOrEmptyDirectory_Throws()
    {
        // Mirrors JsonDailyLogger's contract: a missing log directory must
        // be rejected loudly at construction, not at the first Append.
        Assert.Throws<ArgumentException>(() => new XmlDailyLogger(""));
        Assert.Throws<ArgumentException>(() => new XmlDailyLogger("   "));
    }

    [Fact]
    public void Append_NullEntry_Throws()
    {
        IDailyLogger logger = new XmlDailyLogger(_tempDir);

        Assert.Throws<ArgumentNullException>(() => logger.Append(null!));
    }

    [Fact]
    public void Append_FromMultipleThreads_NoEntryLost()
    {
        // XmlDailyLogger uses a write lock for the same reason JsonDailyLogger
        // does: backup jobs run concurrently and must not corrupt the daily
        // file or drop entries. Two threads x 25 entries each is enough to
        // surface a regression on the lock without making the test slow.
        IDailyLogger logger = new XmlDailyLogger(_tempDir);
        const int perThread = 25;

        var threads = new[]
        {
            new Thread(() => { for (int i = 0; i < perThread; i++) logger.Append(NewEntry($"A-{i}")); }),
            new Thread(() => { for (int i = 0; i < perThread; i++) logger.Append(NewEntry($"B-{i}")); }),
        };

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        var doc = XDocument.Load(DailyFilePath());
        Assert.Equal(perThread * 2, doc.Root!.Elements("Entry").Count());
    }

    private static LogEntry NewEntry(string jobName) => new()
    {
        Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        JobName = jobName,
        SourceFile = "/tmp/src",
        TargetFile = "/tmp/dst",
        FileSize = 1,
        FileTransferTimeMs = 1,
    };

    [Fact]
    public void Append_CreatesDailyFileWithXmlExtension()
    {
        var logger = new XmlDailyLogger(_tempDir);

        logger.Append(new LogEntry { JobName = "ext-test", FileTransferTimeMs = 1 });

        Assert.True(File.Exists(DailyFilePath()));
    }

    [Fact]
    public void Append_OmitsEncryptionTimeMs_WhenNull()
    {
        // Mirrors JsonDailyLogger: a non-encrypted entry must produce the v1
        // element set so consumers using the schema without the v2 field
        // (or scanning the file by hand) see the same shape they always saw.
        var logger = new XmlDailyLogger(_tempDir);

        logger.Append(new LogEntry { JobName = "no-encrypt", FileTransferTimeMs = 1 });

        var doc = XDocument.Load(DailyFilePath());
        var entry = doc.Root!.Element("Entry");
        Assert.NotNull(entry);
        Assert.Null(entry!.Element("EncryptionTimeMs"));
    }

    [Fact]
    public void Append_IncludesEncryptionTimeMs_WhenSet()
    {
        var logger = new XmlDailyLogger(_tempDir);

        logger.Append(new LogEntry { JobName = "encrypt", EncryptionTimeMs = 17 });

        var doc = XDocument.Load(DailyFilePath());
        var encryption = doc.Root!.Element("Entry")?.Element("EncryptionTimeMs");
        Assert.NotNull(encryption);
        Assert.Equal("17", encryption!.Value);
    }

    [Fact]
    public void Append_AccumulatesEntriesAcrossMultipleCalls()
    {
        // Append-only contract: every Append must extend the daily file,
        // never replace it. A regression here would silently drop earlier
        // entries on the same business day.
        var logger = new XmlDailyLogger(_tempDir);

        logger.Append(new LogEntry { JobName = "first", FileTransferTimeMs = 1 });
        logger.Append(new LogEntry { JobName = "second", FileTransferTimeMs = 2 });
        logger.Append(new LogEntry { JobName = "third", FileTransferTimeMs = 3, EncryptionTimeMs = 10 });

        var doc = XDocument.Load(DailyFilePath());
        var jobNames = doc.Root!.Elements("Entry")
            .Select(e => e.Element("JobName")?.Value)
            .ToArray();

        Assert.Equal(new[] { "first", "second", "third" }, jobNames);
    }

    [Fact]
    public void Append_ProducesXsdValidDocument()
    {
        // Mixed v1-shaped and v2-shaped entries must both pass the schema
        // shipped in the EasyLog assembly. This locks the contract that
        // external ProSoft consumers will validate against.
        var logger = new XmlDailyLogger(_tempDir);
        logger.Append(new LogEntry { JobName = "plain", FileTransferTimeMs = 1 });
        logger.Append(new LogEntry { JobName = "encrypted", EncryptionTimeMs = 42 });

        var doc = XDocument.Load(DailyFilePath());

        var schemas = new XmlSchemaSet();
        schemas.Add(XmlFormatter.LoadSchema());
        doc.Validate(schemas, (sender, e) =>
            throw new XmlSchemaValidationException(e.Message));
    }

    [Fact]
    public void Append_QuarantinesCorruptedFile_AndStartsFresh()
    {
        // A corrupted daily file must be moved aside (not overwritten) so
        // operators can investigate. The next Append must succeed against
        // a brand-new daily file.
        var dailyFile = DailyFilePath();
        File.WriteAllText(dailyFile, "not valid xml <<");

        var logger = new XmlDailyLogger(_tempDir);
        logger.Append(new LogEntry { JobName = "after-corruption", FileTransferTimeMs = 1 });

        var doc = XDocument.Load(dailyFile);
        Assert.Equal("after-corruption", doc.Root!.Element("Entry")?.Element("JobName")?.Value);

        var quarantined = Directory.GetFiles(_tempDir, "*.corrupted-*");
        Assert.Single(quarantined);
    }

    [Fact]
    public void JsonAndXmlDailyFiles_CoexistInTheSameDirectory()
    {
        // The recette spec requires that switching format does not erase
        // the previous run's log. Different extensions guarantee that.
        var jsonLogger = new JsonDailyLogger(_tempDir);
        var xmlLogger = new XmlDailyLogger(_tempDir);

        jsonLogger.Append(new LogEntry { JobName = "json-side", FileTransferTimeMs = 1 });
        xmlLogger.Append(new LogEntry { JobName = "xml-side", FileTransferTimeMs = 2 });

        var jsonFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        var xmlFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.xml");
        Assert.True(File.Exists(jsonFile));
        Assert.True(File.Exists(xmlFile));
    }
}
