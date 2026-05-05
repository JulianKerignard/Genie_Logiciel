using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EasyLog;
using EasySave.Shared;

namespace EasySave.Services;

public sealed class TcpRemoteConsoleServer : IRemoteConsoleServer
{
    private readonly IDailyLogger _logger;
    private readonly ConcurrentDictionary<string, ClientEntry> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    private sealed class ClientEntry(TcpClient client, StreamWriter writer)
    {
        public TcpClient Client { get; } = client;
        public StreamWriter Writer { get; } = writer;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    public event Func<CommandDto, Task> CommandReceived = _ => Task.CompletedTask;

    public TcpRemoteConsoleServer(IDailyLogger logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(int port, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleClientAsync(client, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_cts.Token.IsCancellationRequested) { }
        finally
        {
            _listener.Stop();
        }
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        foreach (var (key, entry) in _clients)
        {
            try { entry.Client.Close(); } catch { }
            _clients.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    public async Task BroadcastAsync(EventDto evt)
    {
        var json = JsonSerializer.Serialize(evt);
        var dead = new List<string>();

        foreach (var (key, entry) in _clients)
        {
            await entry.WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await entry.Writer.WriteLineAsync(json).ConfigureAwait(false);
                await entry.Writer.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                dead.Add(key);
            }
            finally
            {
                entry.WriteLock.Release();
            }
        }

        foreach (var key in dead)
        {
            if (_clients.TryRemove(key, out var removed))
            {
                try { removed.Client.Close(); } catch { }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var stream = client.GetStream();
        var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = false };
        var entry = new ClientEntry(client, writer);
        _clients[endpoint] = entry;

        using var reader = new StreamReader(stream, leaveOpen: true);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                CommandDto? cmd;
                try { cmd = JsonSerializer.Deserialize<CommandDto>(line); }
                catch (JsonException) { continue; }

                if (cmd is null) continue;

                cmd = cmd with { SourceIp = endpoint };

                // Fire each handler in isolation per the interface contract.
                foreach (var handler in CommandReceived.GetInvocationList().Cast<Func<CommandDto, Task>>())
                {
                    try { await handler(cmd).ConfigureAwait(false); }
                    catch { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            _clients.TryRemove(endpoint, out _);
            try { client.Close(); } catch { }
        }
    }
}
