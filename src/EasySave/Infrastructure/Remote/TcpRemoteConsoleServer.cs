using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EasyLog;
using EasySave.Services;
using EasySave.Shared;

namespace EasySave.Infrastructure.Remote;

public sealed class TcpRemoteConsoleServer : IRemoteConsoleServer
{
    private readonly IDailyLogger _logger;
    private readonly ConcurrentDictionary<string, ClientEntry> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public event Func<CommandDto, Task>? CommandReceived;

    public TcpRemoteConsoleServer(IDailyLogger logger) => _logger = logger;

    public async Task StartAsync(int port, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        while (!_cts.Token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }

            _ = HandleClientAsync(client, _cts.Token);
        }
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        foreach (var key in _clients.Keys)
            RemoveClient(key);
        return Task.CompletedTask;
    }

    public async Task BroadcastAsync(EventDto evt)
    {
        var json = JsonSerializer.Serialize(evt);
        var dead = new List<string>();

        foreach (var (key, entry) in _clients)
        {
            await entry.WriteLock.WaitAsync();
            try
            {
                await entry.Writer.WriteLineAsync(json);
                await entry.Writer.FlushAsync();
            }
            catch { dead.Add(key); }
            finally { entry.WriteLock.Release(); }
        }

        foreach (var key in dead)
            RemoveClient(key);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var key = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
        var stream = client.GetStream();
        var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true)
            { AutoFlush = false };
        _clients[key] = new ClientEntry(client, writer);

        _logger.Append(new LogEntry
        {
            Timestamp = DateTimeOffset.Now.ToString("o"),
            JobName = string.Empty,
            SourceFile = key,
            TargetFile = string.Empty,
            FileSize = 0,
            FileTransferTimeMs = 0,
            EventType = LogEvent.RemoteConsoleConnected,
        });

        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, -1, leaveOpen: true);
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(ct); }
                catch (OperationCanceledException) { break; }
                if (line is null) break;

                CommandDto? cmd;
                try { cmd = JsonSerializer.Deserialize<CommandDto>(line); }
                catch { continue; }
                if (cmd is null) continue;

                cmd = cmd with { SourceIp = key };
                await FireCommandReceivedAsync(cmd);
            }
        }
        catch { /* network disconnect */ }
        finally { RemoveClient(key); }
    }

    private async Task FireCommandReceivedAsync(CommandDto cmd)
    {
        var handler = CommandReceived;
        if (handler is null) return;
        foreach (var h in handler.GetInvocationList().Cast<Func<CommandDto, Task>>())
        {
            try { await h(cmd); }
            catch { /* isolate faulted subscribers */ }
        }
    }

    private void RemoveClient(string key)
    {
        if (!_clients.TryRemove(key, out var entry)) return;

        _logger.Append(new LogEntry
        {
            Timestamp = DateTimeOffset.Now.ToString("o"),
            JobName = string.Empty,
            SourceFile = key,
            TargetFile = string.Empty,
            FileSize = 0,
            FileTransferTimeMs = 0,
            EventType = LogEvent.RemoteConsoleDisconnected,
        });

        try { entry.Client.Close(); } catch { }
    }

    private sealed class ClientEntry(TcpClient client, StreamWriter writer)
    {
        public TcpClient Client { get; } = client;
        public StreamWriter Writer { get; } = writer;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }
}
