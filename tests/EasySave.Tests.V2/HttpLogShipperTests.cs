using System.Net;
using System.Net.Http;
using System.Text.Json;
using EasyLog;

namespace EasySave.Tests.V2;

/// <summary>
/// Unit tests for <see cref="HttpLogShipper"/> and the <see cref="LogMode"/>
/// routing on <see cref="JsonDailyLogger"/>. The shipper is exercised
/// through a custom <see cref="HttpMessageHandler"/> so tests never bind a
/// real TCP socket (keeps them deterministic on CI runners).
/// </summary>
public class HttpLogShipperTests : IDisposable
{
    private readonly string _tempDir;

    public HttpLogShipperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "easylog-shipper-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Append_SendsEntry_AsJsonPostToEndpoint()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        shipper.Append(new LogEntry
        {
            Timestamp = "2026-05-12T10:00:00+02:00",
            JobName = "demo",
            SourceFile = @"C:\src\file.txt",
            TargetFile = @"C:\dst\file.txt",
            FileSize = 42,
            FileTransferTimeMs = 3,
        });

        // Single drain — the background task is fire-and-forget so we wait
        // for the request to be observed instead of relying on a sleep.
        await handler.WaitForRequestsAsync(1);

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("http://collector.local/logs", handler.Requests[0].Uri);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("MachineName", out _));
        Assert.True(root.TryGetProperty("UserName", out _));
        Assert.Equal("demo", root.GetProperty("Entry").GetProperty("JobName").GetString());
    }

    [Fact]
    public async Task Append_DoesNotBlockCaller_WhenCollectorIsDown()
    {
        // Simulate a network outage: every send fails with HttpRequestException.
        var handler = new RecordingHandler(_ => throw new HttpRequestException("dns failure"));
        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        // 50 calls in a tight loop must return instantly — none of them
        // performs network I/O on the caller's thread.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            shipper.Append(new LogEntry { JobName = $"job-{i}", Timestamp = "x" });
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Append blocked on a downed collector ({sw.ElapsedMilliseconds}ms for 50 calls).");
    }

    [Fact]
    public async Task Append_FlushesBufferedEntries_AfterCollectorRecovers()
    {
        // First 3 attempts on the in-flight entry fail, the 4th succeeds.
        // The shipper must keep retrying without losing the entry.
        int callCount = 0;
        var handler = new RecordingHandler(_ =>
        {
            callCount++;
            if (callCount <= 3) throw new HttpRequestException("transient");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        shipper.Append(new LogEntry { JobName = "retry-me", Timestamp = "t" });

        // Three failures back off 1s + 2s + 5s = 8s before the 4th attempt
        // succeeds. Cap the test at 15s so a hung loop fails quickly without
        // making CI flaky on a slow runner.
        await handler.WaitForRequestsAsync(4, TimeSpan.FromSeconds(15));

        Assert.Equal(4, callCount);
        Assert.Equal("retry-me",
            JsonDocument.Parse(handler.Requests[3].Body)
                .RootElement.GetProperty("Entry")
                .GetProperty("JobName").GetString());
    }

    [Fact]
    public void Constructor_Rejects_NonHttpEndpoint()
    {
        Assert.Throws<ArgumentException>(() => new HttpLogShipper(new Uri("ftp://x/y")));
    }

    [Fact]
    public async Task JsonDailyLogger_LocalMode_DoesNotCallShipper()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        using (var logger = new JsonDailyLogger(_tempDir, shipper, LogMode.Local))
        {
            logger.Append(new LogEntry { JobName = "local-only", Timestamp = "t" });
        }

        // Local mode must never reach the shipper, even when one is wired.
        await Task.Delay(200);
        Assert.Empty(handler.Requests);
        Assert.Single(Directory.GetFiles(_tempDir, "*.json"));
    }

    [Fact]
    public async Task JsonDailyLogger_CentralizedMode_SkipsLocalFile()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        using (var logger = new JsonDailyLogger(_tempDir, shipper, LogMode.Centralized))
        {
            logger.Append(new LogEntry { JobName = "central-only", Timestamp = "t" });
        }

        await handler.WaitForRequestsAsync(1);
        Assert.Single(handler.Requests);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.json"));
    }

    [Fact]
    public async Task JsonDailyLogger_BothMode_WritesLocalAndShips()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        using (var logger = new JsonDailyLogger(_tempDir, shipper, LogMode.Both))
        {
            logger.Append(new LogEntry { JobName = "both", Timestamp = "t" });
        }

        await handler.WaitForRequestsAsync(1);
        Assert.Single(handler.Requests);
        Assert.Single(Directory.GetFiles(_tempDir, "*.json"));
    }

    [Fact]
    public void JsonDailyLogger_FallsBackToLocal_WhenShipperIsNullButModeIsCentralized()
    {
        // Misconfiguration guard: a logger created with Centralized but no
        // shipper must NOT silently drop entries — it falls back to Local.
        using var logger = new JsonDailyLogger(_tempDir, shipper: null, mode: LogMode.Centralized);
        logger.Append(new LogEntry { JobName = "guard", Timestamp = "t" });

        Assert.Single(Directory.GetFiles(_tempDir, "*.json"));
    }

    [Fact]
    public async Task DisposeAsync_DoesNotBlockIndefinitely_OnDownCollector()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("down"));
        var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        shipper.Append(new LogEntry { JobName = "stuck", Timestamp = "t" });

        // Give the writer task a moment to start its first attempt.
        await Task.Delay(200);

        // DisposeAsync must cancel the in-flight backoff and complete quickly.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await shipper.DisposeAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"DisposeAsync took {sw.ElapsedMilliseconds}ms with a downed collector.");
    }

    [Fact]
    public async Task Append_FlushesBufferedEntries_InProducerOrder_AfterReconnect()
    {
        // Producer enqueues N entries while the collector is down. Once the
        // collector recovers, every entry must arrive in the exact order
        // the producer called Append. Contract from the CdC requirement:
        // a re-connection replays the buffer in FIFO order so the central
        // file mirrors the producer's local timeline.
        //
        // Use 3 failures only (backoff 1+2+5 = 8 s before the 4th attempt
        // succeeds) — 4+ failures push the backoff past 30 s cap which
        // would make the test take ~60 s on a passing run.
        const int failuresBeforeRecovery = 3;
        int attempt = 0;
        var handler = new RecordingHandler(_ =>
        {
            int current = Interlocked.Increment(ref attempt);
            if (current <= failuresBeforeRecovery)
                throw new HttpRequestException("network down");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        const int totalEntries = 10;
        for (int i = 0; i < totalEntries; i++)
        {
            shipper.Append(new LogEntry { JobName = $"entry-{i:D2}", Timestamp = "t" });
        }

        // Total expected handler calls = failuresBeforeRecovery (all on
        // entry 0) + totalEntries (all successful). Backoff tops out at
        // ~8 s before the recovery; cap the wait at 20 s for CI jitter.
        int expectedHandlerCalls = failuresBeforeRecovery + totalEntries;
        await handler.WaitForRequestsAsync(expectedHandlerCalls, TimeSpan.FromSeconds(20));

        // Successful POSTs come after the failures, in FIFO order.
        var successfulBodies = handler.Requests
            .Skip(failuresBeforeRecovery)
            .Take(totalEntries)
            .Select(r => JsonDocument.Parse(r.Body)
                .RootElement.GetProperty("Entry").GetProperty("JobName").GetString())
            .ToArray();

        var expected = Enumerable.Range(0, totalEntries)
            .Select(i => $"entry-{i:D2}").ToArray();
        Assert.Equal(expected, successfulBodies);
    }

    [Fact]
    public async Task Append_LosesNoEntries_DuringExtendedOutage_AndRecovery()
    {
        // Explicit zero-loss assertion: 100 entries enqueued during an
        // outage, collector recovers, every single one must be POSTed.
        // count_expected == count_received.
        //
        // 3 failures keeps backoff under the 30 s cap (1+2+5 = 8 s) — see
        // the order-preservation test above for the rationale.
        const int entryCount = 100;
        const int failuresBeforeRecovery = 3;

        int attempt = 0;
        var handler = new RecordingHandler(_ =>
        {
            int current = Interlocked.Increment(ref attempt);
            if (current <= failuresBeforeRecovery)
                throw new HttpRequestException("transient outage");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        for (int i = 0; i < entryCount; i++)
        {
            shipper.Append(new LogEntry { JobName = $"loss-test-{i:D3}", Timestamp = "t" });
        }

        // Total attempts = failuresBeforeRecovery (retries on entry 0)
        // + entryCount (successful POSTs for entries 0..N-1).
        await handler.WaitForRequestsAsync(
            failuresBeforeRecovery + entryCount,
            TimeSpan.FromSeconds(20));

        int successful = handler.Requests.Skip(failuresBeforeRecovery).Count();
        Assert.Equal(entryCount, successful);

        // Defense in depth: verify every JobName landed exactly once.
        var receivedJobs = handler.Requests
            .Skip(failuresBeforeRecovery)
            .Select(r => JsonDocument.Parse(r.Body)
                .RootElement.GetProperty("Entry").GetProperty("JobName").GetString())
            .ToHashSet();
        var expectedJobs = Enumerable.Range(0, entryCount)
            .Select(i => $"loss-test-{i:D3}")
            .ToHashSet();
        Assert.Equal(expectedJobs, receivedJobs);
    }

    [Fact]
    public async Task Append_SustainsOneThousandEntriesPerSecond_OnCaller()
    {
        // Throughput contract: a backup job posting at 1000 entries / s
        // must not block its caller. Backups in v3 can fire entries from
        // multiple worker threads in parallel; if Append took even a few
        // hundred microseconds per call, the worker pool would spend more
        // time queueing logs than copying files. Note we measure the
        // CALLER side (Append) — the network drain is async and its rate
        // is bounded by the collector, not by us.
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent));
        await using var shipper = new HttpLogShipper(
            new Uri("http://collector.local/logs"),
            new HttpClient(handler));

        const int entriesPerSecond = 1000;
        const int seconds = 1;
        const int totalEntries = entriesPerSecond * seconds;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < totalEntries; i++)
        {
            shipper.Append(new LogEntry { JobName = $"perf-{i}", Timestamp = "t" });
        }
        sw.Stop();

        // Append must keep up with 1000/s, so 1000 entries should finish in
        // well under 1 s. 500 ms gives 5× headroom against CI jitter — if a
        // future refactor adds disk I/O or contention on the Append path
        // this test fails sharply.
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Append blocked at {totalEntries / Math.Max(sw.ElapsedMilliseconds / 1000.0, 0.001):F0} entries/s " +
            $"(observed {sw.ElapsedMilliseconds} ms for {totalEntries} calls; target >= 1000/s).");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Recording handler — captures outgoing requests so tests can assert
    // method, URI and JSON body without spinning a real HTTP listener.
    // ──────────────────────────────────────────────────────────────────────
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        private readonly List<RecordedRequest> _requests = new();
        private readonly object _lock = new();
        private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        public IReadOnlyList<RecordedRequest> Requests
        {
            get { lock (_lock) return _requests.ToArray(); }
        }

        public async Task WaitForRequestsAsync(int count, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    if (_requests.Count >= count) return;
                }
                await Task.WhenAny(_signal.Task, Task.Delay(100));
            }
            lock (_lock)
            {
                if (_requests.Count < count)
                    throw new Xunit.Sdk.XunitException(
                        $"Expected {count} requests, observed {_requests.Count} within timeout.");
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (_lock)
            {
                _requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri?.ToString() ?? "",
                    body));
                var old = _signal;
                _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                old.TrySetResult();
            }

            // Throw / return based on the test factory. The factory runs
            // outside the lock so it can throw freely without deadlocking.
            return _factory(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string Body);
}
