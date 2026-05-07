using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EasySave.RemoteConsole.Abstractions;
using EasySave.Shared;

namespace EasySave.RemoteConsole.Infrastructure;

public sealed class TcpRemoteConsoleClient : IRemoteConsoleClient, IAsyncDisposable
{
    private static readonly int[] BackoffSeconds = [1, 2, 5, 10, 10, 10];

    private readonly SimpleSubject<RemoteConnectionState> _stateSubject = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private TcpClient? _tcp;
    private StreamWriter? _writer;
    private string _host = string.Empty;
    private int _port;

    public event Func<EventDto, Task>? EventReceived;
    public IObservable<RemoteConnectionState> ConnectionState => _stateSubject;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        // Cancel and release any existing connection before opening a new one.
        _cts?.Cancel();
        _cts?.Dispose();
        _tcp?.Close();
        _tcp = null;

        _host = host;
        _port = port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stateSubject.OnNext(RemoteConnectionState.Connecting);
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, _cts.Token);
        var stream = _tcp.GetStream();
        _writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true)
        { AutoFlush = false };
        _stateSubject.OnNext(RemoteConnectionState.Connected);
        _ = ReadLoopAsync(_cts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var reader = new StreamReader(
                    _tcp!.GetStream(), Encoding.UTF8, false, -1, leaveOpen: true);
                while (!ct.IsCancellationRequested)
                {
                    string? line;
                    try { line = await reader.ReadLineAsync(ct); }
                    catch (OperationCanceledException) { return; }
                    if (line is null) break;

                    attempt = 0;
                    EventDto? evt;
                    try { evt = JsonSerializer.Deserialize<EventDto>(line); }
                    catch { continue; }
                    if (evt is null) continue;

                    await FireEventReceivedAsync(evt);
                }
            }
            catch (OperationCanceledException) { return; }
            catch { /* network disconnect — fall through to reconnect */ }

            if (ct.IsCancellationRequested) return;
            _stateSubject.OnNext(RemoteConnectionState.Disconnected);

            var delay = BackoffSeconds[Math.Min(attempt, BackoffSeconds.Length - 1)];
            attempt++;
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); }
            catch (OperationCanceledException) { return; }

            _stateSubject.OnNext(RemoteConnectionState.Connecting);
            try
            {
                _tcp?.Close();
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(_host, _port, ct);
                var stream = _tcp.GetStream();
                await _writeLock.WaitAsync(ct);
                try
                {
                    _writer?.Dispose();
                    _writer = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: true)
                    { AutoFlush = false };
                }
                finally { _writeLock.Release(); }
                _stateSubject.OnNext(RemoteConnectionState.Connected);
            }
            catch (OperationCanceledException) { return; }
            catch { _stateSubject.OnNext(RemoteConnectionState.Error); }
        }
    }

    private async Task FireEventReceivedAsync(EventDto evt)
    {
        var handler = EventReceived;
        if (handler is null) return;
        foreach (var h in handler.GetInvocationList().Cast<Func<EventDto, Task>>())
        {
            try { await h(evt); } catch { }
        }
    }

    public async Task SendCommandAsync(CommandDto cmd)
    {
        var json = JsonSerializer.Serialize(cmd);
        await _writeLock.WaitAsync();
        try
        {
            if (_writer is null) return;
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        await _writeLock.WaitAsync();
        try { _writer?.Dispose(); }
        finally { _writeLock.Release(); }
        _tcp?.Close();
        _stateSubject.OnNext(RemoteConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
        _writeLock.Dispose();
        _stateSubject.OnCompleted();
    }
}
