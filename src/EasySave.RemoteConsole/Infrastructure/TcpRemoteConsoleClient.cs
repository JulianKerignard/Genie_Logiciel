using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EasySave.Shared;

namespace EasySave.RemoteConsole.Infrastructure;

// TCP client that connects to the EasySave engine's remote console server,
// reads EventDto frames (newline-delimited JSON) and sends CommandDto frames.
// Auto-reconnects with exponential back-off: 1 s, 2 s, 5 s, then 10 s plateau.
public sealed class TcpRemoteConsoleClient : IRemoteConsoleClient
{
    // Back-off delays in milliseconds: 1 s, 2 s, 5 s, 10 s (plateau).
    private static readonly int[] BackoffMs = [1_000, 2_000, 5_000, 10_000];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Initialised in constructor to avoid CS0236 (field initializer cannot reference
    // instance member 'ConnectionState' — same name as the enum type in this namespace).
    private readonly BehaviorSubject<ConnectionState> _stateSubject;

    private TcpClient? _tcp;
    private CancellationTokenSource? _cts;

    public TcpRemoteConsoleClient()
    {
        _stateSubject = new BehaviorSubject<ConnectionState>(Infrastructure.ConnectionState.Disconnected);
    }

    public event Action<EventDto>? EventReceived;
    public IObservable<ConnectionState> ConnectionState => _stateSubject;

    public Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        // Cancel and dispose the previous loop before creating a new one.
        // Interlocked.Exchange atomically swaps the field so no concurrent call
        // can observe a partially-initialised CTS, and the old loop exits at its
        // next cancellable await rather than running alongside the new one.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var prev = Interlocked.Exchange(ref _cts, cts);
        prev?.Cancel();
        prev?.Dispose();

        _stateSubject.OnNext(Infrastructure.ConnectionState.Connecting);
        _ = Task.Run(() => ConnectLoopAsync(host, port, cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        _tcp?.Dispose();
        _tcp = null;
        _stateSubject.OnNext(Infrastructure.ConnectionState.Disconnected);
        _cts?.Dispose();
        _cts = null;
    }

    public Task SendCommandAsync(CommandDto cmd)
    {
        // Capture once to avoid TOCTOU: ConnectLoopAsync or DisconnectAsync can null/dispose
        // _tcp between the null-check and the GetStream() call on the next line.
        var tcp = _tcp;
        if (tcp is null || !tcp.Connected) return Task.CompletedTask;
        try
        {
            var line = JsonSerializer.Serialize(cmd, JsonOptions) + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            return tcp.GetStream().WriteAsync(bytes).AsTask();
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    private async Task ConnectLoopAsync(string host, int port, CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp = new();
            try
            {
                _stateSubject.OnNext(Infrastructure.ConnectionState.Connecting);
                await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
                _tcp = tcp;
                _stateSubject.OnNext(Infrastructure.ConnectionState.Connected);
                attempt = 0;

                // Block until the server closes the connection or ct fires.
                await ReadLoopAsync(tcp, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                tcp.Dispose();
                _tcp = null;
                _stateSubject.OnNext(Infrastructure.ConnectionState.Disconnected);
                return;
            }
            catch
            {
                tcp.Dispose();
                _tcp = null;
                _stateSubject.OnNext(Infrastructure.ConnectionState.Disconnected);
            }

            if (ct.IsCancellationRequested) return;

            var delay = BackoffMs[Math.Min(attempt, BackoffMs.Length - 1)];
            attempt++;

            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ReadLoopAsync(TcpClient tcp, CancellationToken ct)
    {
        using var reader = new StreamReader(tcp.GetStream(), Encoding.UTF8, leaveOpen: true);
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            EventDto? evt = null;
            try { evt = JsonSerializer.Deserialize<EventDto>(line, JsonOptions); }
            catch (JsonException) { }

            if (evt is not null)
                EventReceived?.Invoke(evt);
        }
    }
}
