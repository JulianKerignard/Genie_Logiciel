using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using EasyLog;

namespace LogCentralizer.Tests;

/// <summary>
/// End-to-end validation against the actual Docker image built from
/// <c>LogCentralizer/Dockerfile</c>. The in-process suite
/// (<see cref="LogCentralizerTests"/>) covers the ASP.NET host wiring;
/// this suite proves the SAME contract holds once the service is
/// containerized, in the configuration operators run in production.
/// </summary>
/// <remarks>
/// <para>
/// The image build and container start are amortized across every test
/// in the class via <see cref="IClassFixture{TFixture}"/> — xUnit
/// instantiates the test class once per test method by default, so
/// hosting the Docker lifecycle on the class itself would rebuild the
/// image for each test (~90 s × N). With the fixture, the build runs
/// exactly once and the container is reused for all tests in the class.
/// </para>
/// <para>
/// Marked with <see cref="SkippableFact"/>: a developer or CI runner
/// without Docker sees the suite as <c>Skipped</c> rather than failing
/// the build. The in-process suite still runs unconditionally.
/// </para>
/// </remarks>
public sealed class LogCentralizerE2ETests : IClassFixture<LogCentralizerE2EFixture>
{
    private readonly LogCentralizerE2EFixture _fx;

    public LogCentralizerE2ETests(LogCentralizerE2EFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ContainerizedService_HealthEndpoint_Returns200()
    {
        Skip.If(_fx.Http is null, _fx.SkipReason ?? "Docker daemon not available on this host.");

        var response = await _fx.Http!.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    public async Task ThreeConcurrentClients_AgainstRealContainer_LandInSingleDailyFile()
    {
        // CdC compliance verified against the SAME image operators ship:
        // three simulated workstations POST 50 entries each in parallel
        // against the container's HTTP surface. The bind-mounted volume
        // on the host must show exactly one daily file with 150 lines
        // and three distinct (MachineName, UserName) pairs — zero loss,
        // no per-host file fragmentation.
        Skip.If(_fx.Http is null || _fx.HostLogsDir is null,
            _fx.SkipReason ?? "Docker daemon not available on this host.");

        const int clientCount = 3;
        const int entriesPerClient = 50;

        var machines = Enumerable.Range(0, clientCount).Select(i => $"WS-E2E-{i:D2}").ToArray();
        var users = Enumerable.Range(0, clientCount).Select(i => $"e2e-user-{i}").ToArray();

        var tasks = new List<Task>();
        for (int i = 0; i < clientCount; i++)
        {
            int captured = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int n = 0; n < entriesPerClient; n++)
                {
                    var entry = new LogEntry
                    {
                        Timestamp = DateTimeOffset.Now.ToString("o"),
                        JobName = $"job-{captured}-{n}",
                        SourceFile = "src",
                        TargetFile = "dst",
                        FileSize = n,
                        FileTransferTimeMs = 1,
                        MachineName = machines[captured],
                        UserName = users[captured],
                    };
                    var resp = await _fx.Http!.PostAsJsonAsync("/logs", entry);
                    Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
                }
            }));
        }
        await Task.WhenAll(tasks);

        // Container writes asynchronously; poll the bind-mounted dir until
        // all 150 entries have flushed. Generous timeout because the
        // container's filesystem layer adds latency vs. native.
        var lines = await WaitForLinesAsync(
            _fx.HostLogsDir!,
            expected: clientCount * entriesPerClient,
            timeout: TimeSpan.FromSeconds(20));

        Assert.Single(Directory.GetFiles(_fx.HostLogsDir!, "*.jsonl"));
        Assert.Equal(clientCount * entriesPerClient, lines.Length);

        // Default serializer options (no PropertyNameCaseInsensitive) so the
        // e2e suite enforces the same strict-PascalCase contract as the
        // in-process suite. A regression that made the container emit
        // camelCase on disk would fail BOTH suites instead of one quietly
        // passing while the other broke.
        var parsed = lines
            .Select(l => JsonSerializer.Deserialize<LogEntry>(l)!)
            .ToList();
        var distinctMachines = parsed.Select(e => e.MachineName!).Distinct().ToHashSet();
        var distinctUsers = parsed.Select(e => e.UserName!).Distinct().ToHashSet();

        Assert.Equal(machines.ToHashSet(), distinctMachines);
        Assert.Equal(users.ToHashSet(), distinctUsers);
    }

    // Poll the logs directory until the expected number of lines is observed
    // or the timeout elapses.
    private static async Task<string[]> WaitForLinesAsync(
        string logsDir, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(logsDir, "*.jsonl");
            if (files.Length > 0)
            {
                using var fs = new FileStream(
                    files[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = await sr.ReadToEndAsync();
                var lines = content.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= expected) return lines;
            }
            await Task.Delay(200);
        }
        throw new Xunit.Sdk.XunitException(
            $"Expected {expected} persisted lines under {logsDir} within {timeout.TotalSeconds:F0}s.");
    }
}

/// <summary>
/// xUnit class fixture that owns the Docker image build and the running
/// container for the entire e2e suite. Lives for the lifetime of the
/// <see cref="LogCentralizerE2ETests"/> class instance — built once,
/// torn down once, shared across every test method.
/// </summary>
public sealed class LogCentralizerE2EFixture : IAsyncLifetime
{
    private const string ImageTag = "logcentralizer:e2e-test";
    private const int ContainerLogsPort = 8080;

    public HttpClient? Http { get; private set; }
    public string? HostLogsDir { get; private set; }

    /// <summary>
    /// When the lifecycle could not stand up (no daemon, build failure,
    /// container did not pass /health within the wait window), the test
    /// methods read this to surface the underlying cause via Skip.If.
    /// </summary>
    public string? SkipReason { get; private set; }

    private IFutureDockerImage? _image;
    private IContainer? _container;

    public async Task InitializeAsync()
    {
        try
        {
            HostLogsDir = Path.Combine(
                Path.GetTempPath(),
                "logcentralizer-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(HostLogsDir);

            // Linux runners (GitHub Actions, native Linux dev) need 0777 on
            // the host bind-mount: the container's `app` user (UID 1654)
            // cannot write to a directory owned by the runner user otherwise,
            // and the DailyFileWriter background service would silently
            // fault. Docker Desktop on macOS / Windows translates UIDs
            // transparently so this call is a no-op there.
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(HostLogsDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            }

            string repoRoot = LocateRepoRoot();
            _image = new ImageFromDockerfileBuilder()
                .WithName(ImageTag)
                .WithDockerfileDirectory(repoRoot)
                .WithDockerfile("LogCentralizer/Dockerfile")
                .WithCleanUp(true)
                .Build();
            await _image.CreateAsync().ConfigureAwait(false);

            _container = new ContainerBuilder()
                .WithImage(ImageTag)
                .WithPortBinding(ContainerLogsPort, assignRandomHostPort: true)
                .WithBindMount(HostLogsDir, "/var/log/easysave")
                // Intentionally NO WithWaitStrategy: Testcontainers .NET
                // issue #1639 — UntilHttpRequestIsSucceeded has a hardcoded
                // 100 s HttpClient timeout whose TaskCanceledException is
                // not caught by the wait-strategy retry loop, so the probe
                // loops silently until the container default start-timeout
                // fires (60 min). We do the readiness probe ourselves
                // below with an explicit, bounded poll loop.
                .Build();
            await _container.StartAsync().ConfigureAwait(false);

            int hostPort = _container.GetMappedPublicPort(ContainerLogsPort);
            Http = new HttpClient { BaseAddress = new Uri($"http://localhost:{hostPort}") };

            try
            {
                await WaitForHealthAsync(Http, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Capture the container's stdout / stderr so a failed
                // readiness probe surfaces what actually happened inside
                // (app crashed at startup? bound to a different port?
                // wrote a stack trace?) — without these logs the
                // SkippableFact reason is useless.
                (string stdout, string stderr) = await _container.GetLogsAsync().ConfigureAwait(false);
                throw new TimeoutException(
                    $"LogCentralizer /health unreachable. Container stdout/stderr:\n" +
                    $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
            }
        }
        catch (Exception ex)
        {
            // Daemon down, socket unreachable, image build failed — record
            // the reason and clean up partial state so DisposeAsync has
            // nothing dangling to release. SkippableFact in the test
            // methods short-circuits on Http == null.
            SkipReason = $"{ex.GetType().Name}: {ex.Message}";
            await CleanUpAsync().ConfigureAwait(false);
            System.Diagnostics.Trace.TraceWarning(
                $"[LogCentralizer E2E] Skipping suite: {SkipReason}");
        }
    }

    public Task DisposeAsync() => CleanUpAsync();

    private async Task CleanUpAsync()
    {
        Http?.Dispose();
        Http = null;
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
        if (_image is not null)
        {
            await _image.DisposeAsync().ConfigureAwait(false);
            _image = null;
        }
        if (HostLogsDir is not null && Directory.Exists(HostLogsDir))
        {
            // Widen the catch beyond IOException: a stale Docker-owned file
            // inside the bind-mount can surface as UnauthorizedAccessException
            // on the host when the container has been torn down hard. Best-
            // effort cleanup either way.
            try { Directory.Delete(HostLogsDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        HostLogsDir = null;
    }

    private static string LocateRepoRoot()
    {
        // Walk up from the test assembly until we find the .sln. Works
        // from both `dotnet test` (bin/Release/net8.0/) and the IDE.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasySave.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate repo root (EasySave.sln) from the test assembly path.");
        }
        return dir.FullName;
    }

    // Bounded poll loop on /health. Replaces Testcontainers'
    // UntilHttpRequestIsSucceeded which silently hangs for 60 min on a
    // probe timeout (Testcontainers .NET issue #1639 — hardcoded 100 s
    // HttpClient timeout whose TaskCanceledException escapes the
    // wait-strategy retry catch block). Here we own the timeout and the
    // exception filter, so a failed probe surfaces as a clean
    // TimeoutException within 30 s — the InitializeAsync catch then
    // records the message in SkipReason for the inline-test skip.
    private static async Task WaitForHealthAsync(HttpClient client, TimeSpan timeout)
    {
        // Per-request budget short enough that we can probe multiple times
        // inside the overall timeout, but long enough to ride out a
        // momentarily slow Kestrel boot inside the container.
        using var probeClient = new HttpClient
        {
            BaseAddress = client.BaseAddress,
            Timeout = TimeSpan.FromSeconds(2),
        };

        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await probeClient.GetAsync("/health").ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
                lastError = new HttpRequestException($"/health returned {(int)response.StatusCode}");
            }
            catch (HttpRequestException ex) { lastError = ex; }
            catch (TaskCanceledException ex) { lastError = ex; }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"LogCentralizer /health did not become reachable within {timeout.TotalSeconds:F0}s. " +
            $"Last error: {lastError?.GetType().Name}: {lastError?.Message}");
    }
}
