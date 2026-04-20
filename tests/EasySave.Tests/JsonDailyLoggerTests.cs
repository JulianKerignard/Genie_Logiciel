using System.Text.Json;
using EasyLog;

namespace EasySave.Tests;

public class JsonDailyLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public JsonDailyLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easylog-tests-" + Guid.NewGuid().ToString("N"));
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
    public void Constructor_NullOrEmptyDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JsonDailyLogger(""));
        Assert.Throws<ArgumentException>(() => new JsonDailyLogger("   "));
    }

    [Fact]
    public void Append_CreatesDailyFile()
    {
        IDailyLogger logger = new JsonDailyLogger(_tempDir);

        logger.Append(NewEntry("Job1"));

        var expected = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void Append_TwoEntries_StoresBoth()
    {
        IDailyLogger logger = new JsonDailyLogger(_tempDir);

        logger.Append(NewEntry("Job1"));
        logger.Append(NewEntry("Job2"));

        var entries = ReadEntries();
        Assert.Equal(2, entries.Count);
        Assert.Equal("Job1", entries[0].JobName);
        Assert.Equal("Job2", entries[1].JobName);
    }

    [Fact]
    public void Append_PreservesUncPaths()
    {
        IDailyLogger logger = new JsonDailyLogger(_tempDir);

        logger.Append(new LogEntry
        {
            Timestamp = "2026-04-20T10:00:00",
            JobName = "backup",
            SourceFile = @"\\server\share\src.txt",
            TargetFile = @"\\server\share\dst.txt",
            FileSize = 1024,
            FileTransferTimeMs = 5
        });

        var entry = ReadEntries().Single();
        Assert.Equal(@"\\server\share\src.txt", entry.SourceFile);
        Assert.Equal(@"\\server\share\dst.txt", entry.TargetFile);
    }

    [Fact]
    public void Append_NegativeTransferTime_PreservedAsErrorMarker()
    {
        IDailyLogger logger = new JsonDailyLogger(_tempDir);

        logger.Append(new LogEntry
        {
            JobName = "failed-job",
            FileTransferTimeMs = -1
        });

        Assert.Equal(-1, ReadEntries().Single().FileTransferTimeMs);
    }

    [Fact]
    public void Append_ProducesIndentedJson()
    {
        IDailyLogger logger = new JsonDailyLogger(_tempDir);

        logger.Append(NewEntry("Job1"));

        var raw = File.ReadAllText(CurrentDayFile());
        Assert.Contains("\n", raw);
        Assert.Contains("  \"", raw);
    }

    [Fact]
    public void Append_NeverLeavesTmpFile()
    {
        IDailyLogger logger = new JsonDailyLogger(_tempDir);

        for (int i = 0; i < 5; i++)
        {
            logger.Append(NewEntry($"Job{i}"));
        }

        var leftovers = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(leftovers);
    }

    [Fact]
    public void Append_CorruptedFile_PreservesOldFileUnderNewName()
    {
        var filePath = CurrentDayFile();
        File.WriteAllText(filePath, "{ not valid json }");

        IDailyLogger logger = new JsonDailyLogger(_tempDir);
        logger.Append(NewEntry("recovery"));

        var corrupted = Directory.GetFiles(_tempDir, $"{Path.GetFileName(filePath)}.corrupted-*");
        Assert.Single(corrupted);
        Assert.Contains("not valid json", File.ReadAllText(corrupted[0]));

        var entries = ReadEntries();
        Assert.Single(entries);
        Assert.Equal("recovery", entries[0].JobName);
    }

    [Fact]
    public void Append_FromMultipleThreads_NoEntryLost()
    {
        IDailyLogger logger = new JsonDailyLogger(_tempDir);
        const int perThread = 25;

        var threads = new[]
        {
            new Thread(() => { for (int i = 0; i < perThread; i++) logger.Append(NewEntry($"A-{i}")); }),
            new Thread(() => { for (int i = 0; i < perThread; i++) logger.Append(NewEntry($"B-{i}")); }),
        };

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        var entries = ReadEntries();
        Assert.Equal(perThread * 2, entries.Count);
    }

    private string CurrentDayFile()
        => Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");

    private List<LogEntry> ReadEntries()
    {
        var raw = File.ReadAllText(CurrentDayFile());
        return JsonSerializer.Deserialize<List<LogEntry>>(raw) ?? new();
    }

    private static LogEntry NewEntry(string jobName) => new()
    {
        Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        JobName = jobName,
        SourceFile = @"\\server\share\a.txt",
        TargetFile = @"\\server\share\b.txt",
        FileSize = 10,
        FileTransferTimeMs = 1
    };
}
