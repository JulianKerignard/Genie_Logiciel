using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using EasyLog;

// EasySave v3 centralized log collector. CdC: "un seul et unique fichier
// journalier quel que soit le nombre d'utilisateurs". Full design doc:
// docs/v3-log-centralizer-admin.md. Recap:
//   - POST /logs enqueues on a Channel, returns 204; HTTP path never
//     touches disk so a slow filesystem cannot back-pressure clients.
//   - One BackgroundService writer appends to {dir}/yyyy-MM-dd.jsonl.
//   - Single replica only — concurrent appends from N processes corrupt.

var builder = WebApplication.CreateBuilder(args);

// Keep /health responsive when the writer faults. Paired with
// _queue.Writer.TryComplete(ex) in DailyFileWriter, subsequent POSTs
// trip ChannelClosedException → 503. Operators get two observable
// signals: /health=200 + POST=503.
builder.Services.Configure<HostOptions>(opts =>
    opts.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// WhenWritingNull keeps daily files byte-for-byte compatible with
// EasyLog's local format (a row without MachineName/UserName looks like
// a v1.x row). PropertyNameCaseInsensitive accepts both PascalCase
// (default S.T.J) and camelCase (HttpClient.PostAsJsonAsync default).
var serializerOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
    PropertyNameCaseInsensitive = true,
};

// Unbounded: backup logs are low-volume (one row per file copy) and
// dropping entries would defeat the point of central logging. A
// misbehaving client floods RAM — operators monitor host RSS.
var queue = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = false,
});

builder.Services.AddSingleton(queue);
builder.Services.AddSingleton(serializerOptions);
// IOptions binding so WebApplicationFactory test overrides win over
// appsettings.json (reading the option at startup races the factory).
builder.Services.Configure<LogCentralizerOptions>(
    builder.Configuration.GetSection("LogCentralizer"));
builder.Services.AddHostedService<DailyFileWriter>();

var app = builder.Build();

// /health cheap by design: no disk hit, no channel inspection — so a
// slow filesystem cannot flap the container as unhealthy.
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
    /// from the host as a Docker volume (<c>./logs/</c> by convention).
    /// Override via <c>appsettings.json</c> (<c>LogCentralizer:LogsDirectory</c>)
    /// or the standard ASP.NET Core environment-variable form
    /// <c>LogCentralizer__LogsDirectory</c> — see the admin doc for details.
    /// </summary>
    public string LogsDirectory { get; set; } = "/var/log/easysave";
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
        try
        {
            // CreateDirectory here (not at startup) so the LogsDirectory
            // value injected by WebApplicationFactory in tests is honored.
            Directory.CreateDirectory(_options.LogsDirectory);

            await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                // CancellationToken.None on the write: the entry is already
                // dequeued and acked 204. Cancelling mid-flight would lose
                // it (FlushRemainingAsync no longer sees it). The loop
                // itself exits on ReadAllAsync's next iteration.
                await AppendAsync(entry, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown. Pending entries drained by FlushRemainingAsync.
        }
        catch (Exception ex)
        {
            // Permanent fault (disk full, bind-mount permission denied, …).
            // Complete the channel WITH the exception so every subsequent
            // POST /logs trips ChannelClosedException → 503. Without this,
            // BackgroundServiceExceptionBehavior.Ignore would keep /health
            // green while the writer is dead → silent data loss.
            _queue.Writer.TryComplete(ex);
            throw;
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
                // Shutdown drain is best-effort and PER-ENTRY: one transient
                // I/O failure must not strand every entry still in the queue.
                // Each of them was already acked with a 204 — the client
                // dropped it from its retry buffer, so silently abandoning
                // siblings here would lose them for good. Continue draining;
                // anything that ultimately fails is a hard shutdown casualty.
                continue;
            }
        }
    }
}

// Exposed so the integration test project (LogCentralizer.Tests) can use
// WebApplicationFactory&lt;Program&gt; against the in-process host. Without
// this declaration the generated Program class is internal and the factory
// cannot instantiate it.
public partial class Program { }
