using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyLog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LogCentralizer.Tests;

/// <summary>
/// Integration tests for the LogCentralizer minimal API, exercised through
/// <see cref="WebApplicationFactory{TEntryPoint}"/> so the full pipeline
/// (request → channel → background writer → disk) is covered in-process,
/// without needing a Docker daemon. The Docker e2e validation lives in a
/// separate Testcontainers-backed suite (task 3).
/// </summary>
public class LogCentralizerTests : IDisposable
{
    private readonly string _logsDir;
    private readonly CustomFactory _factory;

    public LogCentralizerTests()
    {
        _logsDir = Path.Combine(Path.GetTempPath(), "logcentralizer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logsDir);
        _factory = new CustomFactory(_logsDir);
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_logsDir))
        {
            try { Directory.Delete(_logsDir, recursive: true); }
            catch (IOException) { /* writer task may still hold the file */ }
        }
    }

    [Fact]
    public async Task GetHealth_Returns200WithStatusOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", body);
    }

    [Fact]
    public async Task PostLogs_AcceptsValidEntry_AndPersistsAsJsonLine()
    {
        using var client = _factory.CreateClient();

        var entry = new LogEntry
        {
            Timestamp = "2026-05-12T10:00:00+02:00",
            JobName = "single-client",
            SourceFile = @"\\nas\src\a.txt",
            TargetFile = @"\\nas\dst\a.txt",
            FileSize = 42,
            FileTransferTimeMs = 5,
            MachineName = "WS-01",
            UserName = "alice",
        };

        var response = await client.PostAsJsonAsync("/logs", entry);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var line = await WaitForLinesAsync(_logsDir, expected: 1);
        var persisted = JsonSerializer.Deserialize<LogEntry>(line[0])!;
        Assert.Equal("single-client", persisted.JobName);
        Assert.Equal("WS-01", persisted.MachineName);
        Assert.Equal("alice", persisted.UserName);
    }

    [Fact]
    public async Task PostLogs_InvalidJson_Returns400()
    {
        using var client = _factory.CreateClient();

        using var content = new StringContent("{not-json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/logs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(_logsDir));
    }

    [Fact]
    public async Task PostLogs_EmptyBody_Returns400()
    {
        using var client = _factory.CreateClient();

        using var content = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/logs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ThreeConcurrentClients_WriteToSingleDailyFile_WithDistinctHostsAndZeroLoss()
    {
        // CdC compliance test: "un seul et unique fichier journalier quel
        // que soit le nombre d'utilisateurs". Three simulated EasySave
        // workstations push 100 entries each in parallel. Verify that:
        //   1. Exactly one file is produced.
        //   2. The file contains 300 lines (no loss, no duplicate).
        //   3. The three (MachineName, UserName) pairs are distinguishable.
        const int clients = 3;
        const int entriesPerClient = 100;

        using var client = _factory.CreateClient();

        var clients_ = Enumerable.Range(0, clients).Select(i => $"WS-{i:D2}").ToArray();
        var users_ = Enumerable.Range(0, clients).Select(i => $"user-{i}").ToArray();

        var tasks = new List<Task>();
        for (int i = 0; i < clients; i++)
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
                        MachineName = clients_[captured],
                        UserName = users_[captured],
                    };
                    var resp = await client.PostAsJsonAsync("/logs", entry);
                    Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
                }
            }));
        }
        await Task.WhenAll(tasks);

        var lines = await WaitForLinesAsync(_logsDir, expected: clients * entriesPerClient);

        Assert.Single(Directory.GetFiles(_logsDir, "*.jsonl"));
        Assert.Equal(clients * entriesPerClient, lines.Length);

        var distinctMachines = lines
            .Select(l => JsonSerializer.Deserialize<LogEntry>(l)!.MachineName!)
            .Distinct()
            .ToHashSet();
        Assert.Equal(clients_.ToHashSet(), distinctMachines);

        var distinctUsers = lines
            .Select(l => JsonSerializer.Deserialize<LogEntry>(l)!.UserName!)
            .Distinct()
            .ToHashSet();
        Assert.Equal(users_.ToHashSet(), distinctUsers);
    }

    [Fact]
    public async Task PersistedFile_KeepsHostFieldsOmitted_WhenSenderDidNotSetThem()
    {
        // Retro-compat: a v1 / v2 client that posts an entry without
        // MachineName / UserName must produce a daily-file row with no
        // host fields (no empty "MachineName": null pollution). Consumers
        // reading the file see the same shape EasyLog 1.0 produced.
        using var client = _factory.CreateClient();

        var entry = new LogEntry
        {
            Timestamp = "2026-05-12T11:00:00+02:00",
            JobName = "v1-style",
            SourceFile = "src",
            TargetFile = "dst",
            FileSize = 1,
            FileTransferTimeMs = 1,
        };
        var response = await client.PostAsJsonAsync("/logs", entry);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var line = (await WaitForLinesAsync(_logsDir, expected: 1))[0];
        Assert.DoesNotContain("MachineName", line);
        Assert.DoesNotContain("UserName", line);
    }

    [Fact]
    public async Task ShipperToCollector_RoundTrip_PreservesEveryField()
    {
        // End-to-end wire-format guard. The shipper (production client)
        // posts a fully-populated LogEntry; the collector deserializes
        // and writes it to disk. Every field must survive the round-trip
        // — JobName, SourceFile, TargetFile, FileSize, FileTransferTimeMs,
        // MachineName, UserName, EncryptionTimeMs. A regression that
        // re-introduces a wrapper envelope (or renames a field) would
        // surface here.
        var httpClient = _factory.CreateClient();
        await using var shipper = new HttpLogShipper(
            new Uri(httpClient.BaseAddress!, "/logs"),
            httpClient);

        var sent = new LogEntry
        {
            Timestamp = "2026-05-12T12:00:00+02:00",
            JobName = "round-trip",
            SourceFile = @"\\nas\src\report.pdf",
            TargetFile = @"\\nas\dst\report.pdf",
            FileSize = 12345,
            FileTransferTimeMs = 42,
            EncryptionTimeMs = 7,
            MachineName = "WS-ROUNDTRIP",
            UserName = "round-tripper",
        };
        shipper.Append(sent);

        var line = (await WaitForLinesAsync(_logsDir, expected: 1))[0];
        var persisted = JsonSerializer.Deserialize<LogEntry>(line)!;

        Assert.Equal(sent.JobName, persisted.JobName);
        Assert.Equal(sent.SourceFile, persisted.SourceFile);
        Assert.Equal(sent.TargetFile, persisted.TargetFile);
        Assert.Equal(sent.FileSize, persisted.FileSize);
        Assert.Equal(sent.FileTransferTimeMs, persisted.FileTransferTimeMs);
        Assert.Equal(sent.EncryptionTimeMs, persisted.EncryptionTimeMs);
        Assert.Equal(sent.MachineName, persisted.MachineName);
        Assert.Equal(sent.UserName, persisted.UserName);
        Assert.Equal(sent.Timestamp, persisted.Timestamp);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    // Poll the logs directory until the expected number of lines is observed
    // or the timeout elapses. Mirrors how an external admin would tail the
    // file — the background writer is async, so an assertion that fires
    // right after PostAsync can race against the channel drain.
    private static async Task<string[]> WaitForLinesAsync(string logsDir, int expected, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(logsDir, "*.jsonl");
            if (files.Length > 0)
            {
                // Read with FileShare.ReadWrite so we never race the writer's
                // open file handle while it appends.
                using var fs = new FileStream(files[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = await sr.ReadToEndAsync();
                // Split on both \r and \n so a Windows CI runner (where the
                // writer emits "\r\n" via Environment.NewLine) does not leave
                // a trailing '\r' on every token — JsonSerializer.Deserialize
                // would throw JsonException on the stray byte.
                var lines = content.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= expected) return lines;
            }
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException(
            $"Expected {expected} persisted lines under {logsDir} within timeout.");
    }

    private sealed class CustomFactory : WebApplicationFactory<Program>
    {
        private readonly string _logsDir;

        public CustomFactory(string logsDir) => _logsDir = logsDir;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Inject the temp directory into the host's configuration so the
            // background writer writes inside the test's scratch space.
            builder.ConfigureAppConfiguration(cb =>
            {
                cb.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LogCentralizer:LogsDirectory"] = _logsDir,
                });
            });
        }
    }
}
