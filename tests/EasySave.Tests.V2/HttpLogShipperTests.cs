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

        // First two backoff steps are 1s + 2s = 3s; the shipper should send
        // successfully on retry #4 around the 4–5s mark. Cap the test at 12s
        // so a hung loop fails quickly.
        await handler.WaitForRequestsAsync(4, TimeSpan.FromSeconds(12));

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
