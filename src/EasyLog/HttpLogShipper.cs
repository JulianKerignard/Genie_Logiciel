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
/// the POST succeeds — no entry is dropped on transient outage. The
/// machine name and current user are attached to every payload so the
/// collector can demultiplex across hosts and operators.
/// </summary>
/// <remarks>
/// <para>
/// The buffer is intentionally unbounded: backup logs are low-volume
/// (one row per file copy) and dropping an entry would defeat the point
/// of central logging. A misconfigured collector that stays down for
/// weeks would leak memory; operators are expected to keep an eye on
/// the host process via the daily file (LogMode.Both) until they trust
/// the collector path.
/// </para>
/// <para>
/// Order is preserved: the channel has SingleReader=true and the writer
/// task processes one entry at a time. Inter-thread interleaving on the
/// producer side mirrors the channel arrival order (no producer-side
/// reordering).
/// </para>
/// </remarks>
public sealed class HttpLogShipper : ILogShipper
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // Exponential backoff sequence requested by the CdC: 1s, 2s, 5s, 10s,
    // then the cap (30s) for every subsequent failure until the collector
    // comes back. Reset to index 0 after any successful POST.
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
    private readonly string _machineName;
    private readonly string _userName;
    private readonly Channel<LogEntry> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writerLoop;
    private volatile bool _disposed;

    /// <summary>
    /// Creates a shipper that POSTs to <paramref name="endpoint"/>. The
    /// supplied <paramref name="http"/> is used as-is; the shipper takes
    /// ownership only when the parameter is null (it then creates and
    /// disposes its own client).
    /// </summary>
    /// <param name="endpoint">Absolute HTTP URI of the centralized collector (e.g. http://logs.local:9100/logs).</param>
    /// <param name="http">Optional pre-configured client (test seam). Disposed only when null was passed.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the endpoint is not an absolute HTTP/HTTPS URI.</exception>
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
        _machineName = Environment.MachineName;
        _userName = Environment.UserName;

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

        if (_disposed)
        {
            // Mirrors JsonDailyLogger: dropping silently post-Dispose is the
            // documented contract on the local writer. Centralized side adopts
            // the same convention so host-shutdown code paths never throw.
            return;
        }

        _queue.Writer.TryWrite(entry);
    }

    private async Task WriterLoopAsync(CancellationToken ct)
    {
        int backoffIndex = 0;
        LogEntry? inflight = null;

        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var entry))
                {
                    inflight = entry;

                    // Retry loop: this entry stays "in flight" until the POST
                    // succeeds or shutdown is requested. Order is preserved —
                    // we never advance to the next queued entry while the
                    // current one is still pending.
                    while (!ct.IsCancellationRequested)
                    {
                        if (await TrySendAsync(entry, ct).ConfigureAwait(false))
                        {
                            backoffIndex = 0;
                            inflight = null;
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
                            // Shutdown arrived while we were backing off. The
                            // outer catch handles re-queueing the in-flight
                            // entry so DisposeAsync gets a chance to drain it.
                            return;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path — the entry currently in flight (if any)
            // never made it across the wire. We deliberately do nothing here:
            // the buffer is in-memory, so a host restart loses unsent entries
            // by design. Local file logging (LogMode.Both) is the operator's
            // safety net during cut-over.
        }
        catch (Exception ex)
        {
            // Belt-and-braces: any unexpected throw in the loop would kill
            // the writer and silently strand the queue. Trace + exit so the
            // host's diagnostic infra sees the regression.
            Trace.TraceError($"[EasyLog] HttpLogShipper writer task crashed: {ex}");
        }
    }

    private async Task<bool> TrySendAsync(LogEntry entry, CancellationToken ct)
    {
        try
        {
            var payload = new CentralizedLogPayload(_machineName, _userName, entry);
            using var content = JsonContent.Create(payload, options: PayloadOptions);
            using var response = await _http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — surface as "not sent" so the caller decides to stop.
            return false;
        }
        catch (HttpRequestException)
        {
            // Network unreachable, DNS failure, TLS handshake refused — keep
            // the entry buffered and let the backoff loop retry.
            return false;
        }
        catch (TaskCanceledException)
        {
            // Per-request timeout from HttpClient.Timeout — same handling as
            // a network failure.
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Drains any in-flight buffer up to the per-request HTTP timeout times
    /// the queue depth. Callers that want a hard ceiling should wrap the
    /// returned task in their own <see cref="Task.WaitAsync(TimeSpan)"/>.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

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

    // JSON envelope sent to the collector. Wraps the v1 LogEntry verbatim
    // so the central side can persist the exact same shape it would read
    // from a local daily file, with two extra demux fields.
    private sealed record CentralizedLogPayload(
        string MachineName,
        string UserName,
        LogEntry Entry);
}
