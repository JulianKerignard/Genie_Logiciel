using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using EasyLog;

// EasySave v3 centralized log collector.
//
// Design goals (CdC requirement: "un seul et unique fichier journalier quel
// que soit le nombre d'utilisateurs"):
//
//  - POST /logs accepts a single LogEntry JSON document, enqueues it on an
//    in-memory Channel and returns 204 immediately. The HTTP path never
//    touches disk — slow filesystems on a noisy host cannot back-pressure
//    the EasySave clients into stalling their backup jobs.
//
//  - A single background writer task drains the Channel and appends each
//    entry as one JSON line to the day's file (yyyy-MM-dd.jsonl). With
//    SingleReader=true there is exactly one thread writing to disk, so
//    concurrent clients never race on the file handle and no entry is
//    interleaved or corrupted.
//
//  - Append-only JSON Lines is the on-disk format: every flush is a single
//    O(n) write of the new bytes (vs. O(file_size) for a JSON array we
//    would have to re-serialize on every append). Tail-readers can stream
//    the file line-by-line.
//
//  - Horizontal scale is intentionally not supported in v1: a single
//    "fichier journalier unique" maps to a single writer process. Run one
//    replica behind a TCP load balancer if you need redundancy at the LB
//    layer — but DO NOT scale up the collector replicas (concurrent appends
//    from two processes would interleave at the byte level).

var builder = WebApplication.CreateBuilder(args);

// JSON serializer for entries that LAND on disk. WhenWritingNull keeps the
// daily file byte-for-byte compatible with EasyLog's local format: a row
// without MachineName/UserName looks exactly like a v1.x row.
var serializerOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
    // Accept either PascalCase (EasyLog's local daily files, default
    // System.Text.Json) or camelCase (HttpClient.PostAsJsonAsync defaults
    // since .NET 7). Writes stay PascalCase so the central daily file
    // looks byte-for-byte like a local EasyLog file when fed back into
    // a downstream reader.
    PropertyNameCaseInsensitive = true,
};

// Unbounded queue. Backup logs are low-volume (one row per file copy) and
// dropping entries would defeat the point of central logging. A misbehaving
// client that posts millions of entries while the disk is slow would grow
// memory — operators are expected to monitor the host's RSS during cut-over.
var queue = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = false,
});

builder.Services.AddSingleton(queue);
builder.Services.AddSingleton(serializerOptions);
// Bind LogCentralizerOptions through the DI container so customizations
// injected by integration tests (WebApplicationFactory's
// ConfigureAppConfiguration) win over appsettings.json. Reading the option
// directly at startup would race the factory and pin /var/log/easysave
// before the test could override it.
builder.Services.Configure<LogCentralizerOptions>(
    builder.Configuration.GetSection("LogCentralizer"));
builder.Services.AddHostedService<DailyFileWriter>();

var app = builder.Build();

// Health probe used by Docker / k8s. Cheap on purpose — no disk hit, no
// channel inspection — so a slow filesystem cannot flap the container as
// unhealthy and trigger a restart loop.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/logs", async (HttpRequest req, Channel<LogEntry> q, JsonSerializerOptions opts, CancellationToken ct) =>
{
    LogEntry? entry;
    try
    {
        entry = await JsonSerializer.DeserializeAsync<LogEntry>(req.Body, opts, ct);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "invalid_json" });
    }

    if (entry is null)
    {
        return Results.BadRequest(new { error = "empty_payload" });
    }

    // WriteAsync on an unbounded channel completes synchronously unless the
    // channel was completed (shutdown). The await keeps the call site clean
    // and surfaces ChannelClosedException as a 503 — clients can retry.
    try
    {
        await q.Writer.WriteAsync(entry, ct);
    }
    catch (ChannelClosedException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.NoContent();
});

await app.RunAsync();

// ───────────────────────────────────────────────────────────────────────────
// Implementation types — kept in Program.cs to keep the service single-file.
// ───────────────────────────────────────────────────────────────────────────

internal sealed class LogCentralizerOptions
{
    /// <summary>
    /// Directory where the collector writes its single daily file. Mounted
    /// from the host as a Docker volume (`./logs/` by convention).
    /// </summary>
    public string LogsDirectory { get; set; } =
        Environment.GetEnvironmentVariable("LOGCENTRALIZER_LOGS_DIR") ?? "/var/log/easysave";
}

// Single background task draining the channel. SingleReader=true means
// "we promise the channel only one thread reads from it" — that promise
// is enforced by having exactly one hosted-service instance pull
// ReadAllAsync. Any future refactor that spawns multiple readers would
// break ordering and need a different design.
internal sealed class DailyFileWriter : BackgroundService
{
    private readonly Channel<LogEntry> _queue;
    private readonly LogCentralizerOptions _options;
    private readonly JsonSerializerOptions _serializer;

    public DailyFileWriter(
        Channel<LogEntry> queue,
        Microsoft.Extensions.Options.IOptions<LogCentralizerOptions> options,
        JsonSerializerOptions serializer)
    {
        _queue = queue;
        _options = options.Value;
        _serializer = serializer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Create the directory here (not at startup) so the value of
        // LogsDirectory injected by WebApplicationFactory in tests is the
        // one actually honored. Idempotent — fine to call every time the
        // service starts.
        Directory.CreateDirectory(_options.LogsDirectory);

        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await AppendAsync(entry, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path. The application is exiting; any entries
            // still in the channel will be drained by the FlushRemaining
            // call below.
        }
        finally
        {
            // Drain whatever the producers managed to push between the last
            // ReadAllAsync iteration and the stop signal. We use a non-
            // cancellable token here so a stuck SIGTERM does not lose the
            // entries we already accepted with a 204.
            await FlushRemainingAsync();
        }
    }

    private async Task AppendAsync(LogEntry entry, CancellationToken ct)
    {
        string filePath = Path.Combine(
            _options.LogsDirectory,
            $"{DateTime.Now:yyyy-MM-dd}.jsonl");

        string line = JsonSerializer.Serialize(entry, _serializer) + Environment.NewLine;
        await File.AppendAllTextAsync(filePath, line, ct);
    }

    private async Task FlushRemainingAsync()
    {
        while (_queue.Reader.TryRead(out var entry))
        {
            try
            {
                await AppendAsync(entry, CancellationToken.None);
            }
            catch (Exception)
            {
                // Shutdown drain is best-effort: a failure here means the
                // host is going down hard and we cannot do better than
                // skip the entry. The shipper's retry/buffer on the client
                // side keeps it alive on the next reconnect.
                break;
            }
        }
    }
}

// Exposed so the integration test project (LogCentralizer.Tests) can use
// WebApplicationFactory&lt;Program&gt; against the in-process host. Without
// this declaration the generated Program class is internal and the factory
// cannot instantiate it.
public partial class Program { }
