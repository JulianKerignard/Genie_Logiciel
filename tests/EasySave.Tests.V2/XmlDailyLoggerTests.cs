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

    // ── Regression: issue #112 — transient lock must NOT quarantine the daily file.
    //
    // The previous catch-all in XmlDailyLogger.ReadExisting routed any IOException
    // (antivirus, OneDrive sync, log viewer holding the file briefly) into the same
    // arm as a real XML parse failure, moved the live file aside as `.corrupted-…`,
    // and started a fresh empty document — fragmenting the day across multiple files.
    // The fix narrows the catch to System.Xml.XmlException so IO failures propagate
    // and the day-file stays whole.
    //
    // Platform note: FileShare.None is enforced exclusively on Windows. On POSIX
    // (Linux / macOS) it is advisory — XDocument.Load opens its own FileStream and
    // is not blocked. The lock-dependent tests below early-return on non-Windows
    // platforms so they don't false-pass on Linux CI runners and don't false-fail
    // when the assertion expects an IOException that never fires.

    [Fact]
    public void Append_PropagatesIOException_WhenFileLocked()
    {
        if (!OperatingSystem.IsWindows()) return;

        var logger = new XmlDailyLogger(_tempDir);

        // Seed a valid daily file we can lock.
        logger.Append(new LogEntry { JobName = "seed", FileTransferTimeMs = 1 });
        var dailyFile = DailyFilePath();

        // Simulate an antivirus / OneDrive scan: open with FileShare.None so the
        // next ReadExisting fails with IOException. The handle is released by
        // 'using' once the test method exits.
        using var lockHandle = new FileStream(
            dailyFile, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() =>
            logger.Append(new LogEntry { JobName = "during-lock", FileTransferTimeMs = 2 }));
    }

    [Fact]
    public void Append_DoesNotQuarantineFile_OnTransientLock()
    {
        if (!OperatingSystem.IsWindows()) return;

        var logger = new XmlDailyLogger(_tempDir);
        logger.Append(new LogEntry { JobName = "seed", FileTransferTimeMs = 1 });
        var dailyFile = DailyFilePath();

        using (var lockHandle = new FileStream(
            dailyFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { logger.Append(new LogEntry { JobName = "blocked", FileTransferTimeMs = 2 }); }
            catch (IOException) { /* expected — see test above */ }
        }

        // After the lock is released, the live daily file must still exist intact
        // and no quarantine snapshot must have been produced.
        Assert.True(File.Exists(dailyFile), "Live daily file must survive a transient lock.");
        Assert.Empty(Directory.GetFiles(_tempDir, "*.corrupted-*"));
    }

    [Fact]
    public void Append_QuarantinesOnXmlException_ButNotOnIOException()
    {
        if (!OperatingSystem.IsWindows()) return;

        var logger = new XmlDailyLogger(_tempDir);
        var dailyFile = DailyFilePath();

        // 1) Genuine XML corruption: the file gets quarantined and a fresh
        //    document is started (existing behaviour, kept).
        File.WriteAllText(dailyFile, "<not></valid");
        logger.Append(new LogEntry { JobName = "after-xml-corruption", FileTransferTimeMs = 1 });
        Assert.Single(Directory.GetFiles(_tempDir, "*.corrupted-*"));

        // 2) Now the file is valid again. A transient lock must not produce
        //    a second quarantine snapshot.
        using (var lockHandle = new FileStream(
            dailyFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { logger.Append(new LogEntry { JobName = "blocked", FileTransferTimeMs = 2 }); }
            catch (IOException) { /* expected */ }
        }

        // Still a single quarantine — the IOException path did not create one.
        Assert.Single(Directory.GetFiles(_tempDir, "*.corrupted-*"));
    }

    // ── V3.1: Channel-based writer pattern (mirrors JsonDailyLogger).
    //
    // The previous XmlDailyLogger reparsed and re-serialized the full DOM under
    // a global lock at every Append, producing O(n²) behaviour on long jobs and
    // a noticeable freeze when the user picked log_format=xml in Settings. The
    // refactor funnels Append calls through a Channel<WriteRequest> consumed by
    // a single writer task that batches concurrent appends into one disk write
    // per day file. These tests lock the new contract: zero loss under heavy
    // concurrency, durability on Dispose, per-thread order preserved.

    [Fact]
    public void Append_StressFromManyThreads_NoEntryLost_FilePerJobBatched()
    {
        // 8 threads × 100 entries = 800 entries. Under the previous global-lock
        // design this would trigger 800 reparse-rewrite cycles; under the
        // Channel pattern it collapses into a small handful of batches. The
        // assertion is correctness (zero loss), not throughput — but the test
        // also catches a regression where the writer task drops a batch on
        // exception or hangs an Append.
        IDailyLogger logger = new XmlDailyLogger(_tempDir);
        const int threadCount = 8;
        const int perThread = 100;

        var threads = Enumerable.Range(0, threadCount).Select(t =>
            new Thread(() =>
            {
                for (int i = 0; i < perThread; i++)
                    logger.Append(NewEntry($"T{t}-{i}"));
            })).ToArray();

        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join();

        var doc = XDocument.Load(DailyFilePath());
        Assert.Equal(threadCount * perThread, doc.Root!.Elements("Entry").Count());
    }

    [Fact]
    public void Append_PreservesPerCallerOrder()
    {
        // Channel writer drains in FIFO order so a single thread that calls
        // Append(A1) → Append(A2) → Append(A3) sees them in that order in
        // the daily file (cross-thread order is intentionally not guaranteed).
        var logger = new XmlDailyLogger(_tempDir);

        for (int i = 0; i < 50; i++)
            logger.Append(NewEntry($"seq-{i:D3}"));

        var doc = XDocument.Load(DailyFilePath());
        var jobNames = doc.Root!.Elements("Entry")
            .Select(e => e.Element("JobName")?.Value)
            .ToArray();

        var expected = Enumerable.Range(0, 50).Select(i => $"seq-{i:D3}").ToArray();
        Assert.Equal(expected, jobNames);
    }

    [Fact]
    public void Append_BlocksUntilWriterTaskFlushes_DurabilityContract()
    {
        // The v1.0 sync contract on Append: when the call returns, the entry
        // is on disk. The new Channel pattern preserves this via
        // TaskCompletionSource — Append blocks on the writer's ack. If a
        // future refactor accidentally returned from Append before the flush,
        // a host crash between Append and the next backup step would lose
        // the entry. Lock that contract here.
        var logger = new XmlDailyLogger(_tempDir);

        logger.Append(new LogEntry { JobName = "synchronous", FileTransferTimeMs = 1 });

        // No sleep, no wait — the file must already contain the entry.
        var doc = XDocument.Load(DailyFilePath());
        Assert.Single(doc.Root!.Elements("Entry"));
        Assert.Equal("synchronous", doc.Root!.Element("Entry")!.Element("JobName")!.Value);
    }

    [Fact]
    public void Dispose_DrainsInFlightEntries_BeforeReturning()
    {
        // Dispose closes the channel and waits on the writer loop. Any entry
        // still queued at Dispose-time must reach disk (the loop drains on
        // WaitToReadAsync false → finally block fallback). Without this, a
        // host shutdown could lose the last few entries.
        var logger = new XmlDailyLogger(_tempDir);
        for (int i = 0; i < 20; i++)
            logger.Append(NewEntry($"pre-dispose-{i:D2}"));

        logger.Dispose();

        var doc = XDocument.Load(DailyFilePath());
        Assert.Equal(20, doc.Root!.Elements("Entry").Count());
    }

    [Fact]
    public void Append_AfterDispose_DropsSilently()
    {
        // Same contract as JsonDailyLogger: post-Dispose Append is a silent
        // no-op so a host-shutdown code path that emits a final log line
        // does not throw across the disposal sequence.
        var logger = new XmlDailyLogger(_tempDir);
        logger.Append(new LogEntry { JobName = "before", FileTransferTimeMs = 1 });
        logger.Dispose();

        // Should not throw.
        logger.Append(new LogEntry { JobName = "after", FileTransferTimeMs = 1 });

        var doc = XDocument.Load(DailyFilePath());
        var jobs = doc.Root!.Elements("Entry").Select(e => e.Element("JobName")?.Value).ToArray();
        Assert.Equal(new[] { "before" }, jobs);
    }
}
