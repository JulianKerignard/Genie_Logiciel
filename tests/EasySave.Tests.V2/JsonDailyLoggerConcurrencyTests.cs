using System.Text.Json;
using EasyLog;

namespace EasySave.Tests.V2;

public class JsonDailyLoggerConcurrencyTests : IDisposable
{
    private readonly string _tempDir;

    public JsonDailyLoggerConcurrencyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"easylog-concurrency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void FourJobs_ThousandEntriesEach_AllPersistedAndJsonValid()
    {
        // V3 spec: 4 concurrent jobs publishing 1000 entries each must produce
        // a 4000-entry JSON file, valid (deserializable as a List<LogEntry>),
        // with per-job order preserved (job A's nth entry strictly before its
        // (n+1)th in the file).
        const int jobCount = 4;
        const int perJob = 1000;
        var jobNames = new[] { "Alpha", "Bravo", "Charlie", "Delta" };

        using (var logger = new JsonDailyLogger(_tempDir))
        {
            Parallel.For(0, jobCount, j =>
            {
                string name = jobNames[j];
                for (int i = 0; i < perJob; i++)
                {
                    logger.Append(new LogEntry
                    {
                        Timestamp = DateTime.Now.ToString("o"),
                        JobName = name,
                        SourceFile = $@"\\nas\share\{name}\src-{i:D4}.bin",
                        TargetFile = $@"\\nas\share\{name}\dst-{i:D4}.bin",
                        FileSize = i,
                        FileTransferTimeMs = 1,
                    });
                }
            });
        }

        // After Dispose, the writer task has drained every queued entry to disk.
        var dayFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        Assert.True(File.Exists(dayFile), "Day file was not created.");

        var raw = File.ReadAllText(dayFile);
        var entries = JsonSerializer.Deserialize<List<LogEntry>>(raw);

        Assert.NotNull(entries);
        Assert.Equal(jobCount * perJob, entries!.Count);

        // Per-job order: filter by job name, then check the embedded index in
        // SourceFile (src-0000, src-0001, …) is strictly increasing.
        foreach (var name in jobNames)
        {
            var indices = entries
                .Where(e => e.JobName == name)
                .Select(e => ExtractIndex(e.SourceFile))
                .ToList();

            Assert.Equal(perJob, indices.Count);
            for (int i = 0; i < indices.Count - 1; i++)
            {
                Assert.True(indices[i] < indices[i + 1],
                    $"Per-job order violated for {name}: entry at slot {i} has index {indices[i]} but next has {indices[i + 1]}.");
            }
        }
    }

    [Fact]
    public void Append_FromSingleCaller_PreservesInsertionOrder()
    {
        // Even with the channel-based writer, entries from a single caller
        // must reach the file in the exact order they were appended — the
        // "ordre préservé par job" leg of the V3 spec.
        using var logger = new JsonDailyLogger(_tempDir);

        const int n = 200;
        for (int i = 0; i < n; i++)
        {
            logger.Append(new LogEntry
            {
                Timestamp = DateTime.Now.ToString("o"),
                JobName = "solo",
                SourceFile = $@"\\nas\share\src-{i:D4}.bin",
                FileSize = i,
                FileTransferTimeMs = 1,
            });
        }
        logger.Dispose();

        var raw = File.ReadAllText(Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json"));
        var entries = JsonSerializer.Deserialize<List<LogEntry>>(raw)!;
        Assert.Equal(n, entries.Count);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(i, ExtractIndex(entries[i].SourceFile));
        }
    }

    [Fact]
    public void Dispose_DrainsInFlightEntriesBeforeReturning()
    {
        // Durability contract: every Append that returned successfully must
        // be on disk after the next Dispose (we cannot check this from
        // Append alone, but can verify the count post-Dispose).
        var logger = new JsonDailyLogger(_tempDir);

        const int n = 50;
        for (int i = 0; i < n; i++)
        {
            logger.Append(new LogEntry { JobName = $"j-{i}", FileTransferTimeMs = 1 });
        }
        logger.Dispose();

        var raw = File.ReadAllText(Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json"));
        var entries = JsonSerializer.Deserialize<List<LogEntry>>(raw)!;
        Assert.Equal(n, entries.Count);
    }

    [Fact]
    public void Append_AfterDispose_DoesNotThrow()
    {
        // Shutdown order is not always controllable in the host; a stray
        // Append after Dispose must drop silently rather than crash.
        var logger = new JsonDailyLogger(_tempDir);
        logger.Dispose();
        logger.Append(new LogEntry { JobName = "post-dispose" });
    }

    private static int ExtractIndex(string sourceFile)
    {
        // SourceFile pattern: ...src-NNNN.bin (or after the path-normalizer's
        // \\?\ prefix and forward-slash variants — we just grab the digits
        // immediately before ".bin").
        int dot = sourceFile.LastIndexOf(".bin", StringComparison.Ordinal);
        if (dot < 0) return -1;
        int dash = sourceFile.LastIndexOf('-', dot);
        if (dash < 0) return -1;
        return int.Parse(sourceFile.AsSpan(dash + 1, dot - dash - 1));
    }

    // ── Regression: issue #155 ──────────────────────────────────────────────
    //
    // Pre-fix, ReadExisting could throw IOException (transient AV / OneDrive /
    // log-viewer read lock on the first append of the day) outside FlushBatch's
    // only try/catch. The exception killed the writer task; every subsequent
    // Append then hung forever on its TaskCompletionSource.
    //
    // Windows-only because Unix file locks are advisory: File.ReadAllText is
    // not blocked by FileShare.None on Linux / macOS, so the IOException never
    // fires and the test cannot reproduce the bug.

    [Fact]
    public async Task Append_PropagatesIOException_WhenDayFileLocked_AndDoesNotHang()
    {
        if (!OperatingSystem.IsWindows()) return;

        var dayFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        File.WriteAllText(dayFile, "[]");

        using var locker = new FileStream(dayFile, FileMode.Open, FileAccess.Read, FileShare.None);
        using var logger = new JsonDailyLogger(_tempDir);

        var entry = new LogEntry { Timestamp = DateTime.Now.ToString("o"), JobName = "Foo" };

        // The call must finish (surface the IOException) within a couple
        // seconds — pre-fix it hung forever because the writer task was dead.
        var task = Task.Run(() => logger.Append(entry));
        var ex = await Assert.ThrowsAsync<IOException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Append_KeepsWorking_AfterTransientLockReleased()
    {
        if (!OperatingSystem.IsWindows()) return;

        var dayFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        File.WriteAllText(dayFile, "[]");
        using var logger = new JsonDailyLogger(_tempDir);

        // First append while locked — must throw, not hang. The writer task
        // must survive so the second append can succeed.
        using (new FileStream(dayFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var first = Task.Run(() => logger.Append(new LogEntry { JobName = "during-lock", FileTransferTimeMs = 1 }));
            await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        // Lock released. Next Append must reach disk normally.
        logger.Append(new LogEntry { JobName = "after-lock", FileTransferTimeMs = 2 });
        logger.Dispose();

        var raw = File.ReadAllText(dayFile);
        var entries = JsonSerializer.Deserialize<List<LogEntry>>(raw)!;
        Assert.Contains(entries, e => e.JobName == "after-lock");
    }

    [Fact]
    public async Task Append_DoesNotStrandBatchSiblings_WhenOneGroupFailsToLoad()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Pre-fix, a failing group killed the whole writer task and any
        // subsequent Append in the same process hung forever waiting on its
        // TaskCompletionSource. Fire two consecutive locked-day Appends and
        // assert both surface the IOException — the writer survived the
        // first failure to serve the second one.
        var dayFile = Path.Combine(_tempDir, $"{DateTime.Now:yyyy-MM-dd}.json");
        File.WriteAllText(dayFile, "[]");
        using var locker = new FileStream(dayFile, FileMode.Open, FileAccess.Read, FileShare.None);
        using var logger = new JsonDailyLogger(_tempDir);

        var first = Task.Run(() => logger.Append(new LogEntry { JobName = "first", FileTransferTimeMs = 1 }));
        await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));

        var second = Task.Run(() => logger.Append(new LogEntry { JobName = "second", FileTransferTimeMs = 2 }));
        await Assert.ThrowsAsync<IOException>(() => second.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
