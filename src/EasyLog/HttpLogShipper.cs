using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace EasyLog;

/// <summary>
/// <see cref="ILogShipper"/> that POSTs each <see cref="LogEntry"/> as JSON
/// to a configured HTTP endpoint. Entries are accepted synchronously into
/// an unbounded in-memory <see cref="Channel{T}"/> and a single background
/// task drains the queue. Network failures trigger an exponential backoff
/// (1s, 2s, 5s, 10s, capped at 30s) and entries stay in the buffer until
/// the POST succeeds — no entry is dropped on transient outage.
/// </summary>
/// <remarks>
/// Each <see cref="LogEntry"/> already carries <see cref="LogEntry.MachineName"/>
/// and <see cref="LogEntry.UserName"/> when it reaches the shipper (Json /
/// XmlDailyLogger.Append stamps them from <see cref="Environment"/> before
/// enqueueing). The shipper posts the entry verbatim so the collector
/// receives the exact JSON shape it would read from a local daily file —
/// no wrapper, no field renames, no special demux envelope.
/// </remarks>
public sealed class HttpLogShipper : ILogShipper
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // CdC v3 backoff schedule: 1s, 2s, 5s, 10s, then 30s cap on every
    // subsequent failure. Reset to index 0 after any successful POST.
    private static readonly TimeSpan[] BackoffSchedule =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _endpoint;
    private readonly Channel<LogEntry> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writerLoop;

    // 0 = live, 1 = DisposeAsync entered. Interlocked.CompareExchange so
    // two concurrent DisposeAsync callers cannot both cancel the shutdown
    // CTS (would throw ObjectDisposedException on the second).
    private int _disposed;

    /// <param name="endpoint">Absolute HTTP URI of the centralized collector (e.g. http://logs.local:9100/logs).</param>
    /// <param name="http">Optional pre-configured client (test seam). Disposed only when null was passed.</param>
    public HttpLogShipper(Uri endpoint, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Endpoint must be an absolute http:// or https:// URI.", nameof(endpoint));
        }

        _endpoint = endpoint;
        _http = http ?? new HttpClient();
        _ownsHttpClient = http is null;

        _queue = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _writerLoop = Task.Run(() => WriterLoopAsync(_shutdown.Token));
    }

    /// <inheritdoc />
    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Volatile.Read(ref _disposed) == 1)
        {
            // Mirrors JsonDailyLogger: silent drop post-Dispose so a
            // host-shutdown code path never throws into the caller.
            return;
        }

        _queue.Writer.TryWrite(entry);
    }

    private async Task WriterLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var entry))
                {
                    await ProcessSingleEntryAsync(entry, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown. The in-flight entry (if any) is lost by
            // design — the buffer is in-memory, so a host crash or a
            // Dispose-while-collector-down never persists pending
            // entries. Operators run LogMode.Both during cut-over for
            // exactly this case.
        }
        catch (Exception ex)
        {
            // Belt-and-braces: any unexpected throw kills the writer
            // and silently strands the queue. Trace + exit so the
            // host's diagnostic infra sees the regression.
            Trace.TraceError($"[EasyLog] HttpLogShipper writer task crashed: {ex}");
        }
    }

    // Retries a single entry with exponential backoff until POST succeeds,
    // a permanent error is observed, or shutdown is requested. Order is
    // preserved — we never advance to the next queued entry while the
    // current one is still pending a transient retry.
    private async Task ProcessSingleEntryAsync(LogEntry entry, CancellationToken ct)
    {
        int backoffIndex = 0;
        while (!ct.IsCancellationRequested)
        {
            var outcome = await TrySendAsync(entry, ct).ConfigureAwait(false);
            switch (outcome)
            {
                case SendOutcome.Success:
                    return;
                case SendOutcome.DropPermanent:
                    // 4xx: collector rejected the entry (schema mismatch,
                    // auth, wrong route, payload too large, …). Retrying
                    // can never succeed — drop the entry, trace it for
                    // operators, and advance to the next queued one so
                    // the channel does not grow unbounded behind it.
                    return;
                case SendOutcome.RetryTransient:
                default:
                    break;
            }

            TimeSpan delay = BackoffSchedule[Math.Min(backoffIndex, BackoffSchedule.Length - 1)];
            backoffIndex++;
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Classifies the POST attempt into one of three buckets so
    // ProcessSingleEntryAsync can decide between retry, drop, and advance.
    // 2xx → Success; 5xx / network → RetryTransient; 4xx → DropPermanent.
    private async Task<SendOutcome> TrySendAsync(LogEntry entry, CancellationToken ct)
    {
        try
        {
            using var content = JsonContent.Create(entry, options: PayloadOptions);
            using var response = await _http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return SendOutcome.Success;
            int status = (int)response.StatusCode;
            if (status >= 500)
            {
                return SendOutcome.RetryTransient;
            }
            // 4xx (and any other non-2xx, non-5xx): permanent reject.
            Trace.TraceError(
                $"[EasyLog] HttpLogShipper dropping entry (job='{entry.JobName}', " +
                $"source='{entry.SourceFile}'): collector returned {status} {response.ReasonPhrase}. " +
                $"Retrying would not succeed — fix the endpoint, payload shape, or auth.");
            return SendOutcome.DropPermanent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return SendOutcome.RetryTransient;
        }
        catch (HttpRequestException)
        {
            return SendOutcome.RetryTransient;
        }
        catch (TaskCanceledException)
        {
            // Per-request timeout from HttpClient.Timeout — same handling
            // as a network failure.
            return SendOutcome.RetryTransient;
        }
    }

    private enum SendOutcome
    {
        Success,
        RetryTransient,
        DropPermanent,
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        _queue.Writer.TryComplete();
        _shutdown.Cancel();

        try { await _writerLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Trace.TraceError($"[EasyLog] HttpLogShipper writer task faulted on dispose: {ex}");
        }

        _shutdown.Dispose();
        if (_ownsHttpClient) _http.Dispose();
    }
}
