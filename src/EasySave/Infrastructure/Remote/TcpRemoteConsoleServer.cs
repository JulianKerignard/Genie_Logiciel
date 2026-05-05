using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EasySave.Shared;

namespace EasySave.Infrastructure.Remote;

// TCP server that pushes EventDto frames (newline-delimited JSON) to every
// connected remote console and receives CommandDto frames from them.
// Supports N simultaneous clients; each client is served by its own Task.
public sealed class TcpRemoteConsoleServer : IRemoteConsoleServer
{
    private readonly record struct ClientEntry(TcpClient Client, NetworkStream Stream);

    private readonly ConcurrentDictionary<string, ClientEntry> _clients = new();
    // Tracked so StopAsync can await every reader before returning, preventing
    // a disposed TcpClient from being read/written by an in-flight handler.
    private readonly ConcurrentDictionary<string, Task> _clientTasks = new();

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public event Action<CommandDto>? CommandReceived;

    public Task StartAsync(int port, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch { }
        }

        // Wait for every client handler to drain cleanly after cancellation.
        var pending = _clientTasks.Values.ToArray();
        if (pending.Length > 0)
        {
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch { }
        }

        // Dispose any clients whose handlers failed to clean up.
        foreach (var (_, entry) in _clients)
            entry.Client.Dispose();
        _clients.Clear();
        _clientTasks.Clear();

        _cts?.Dispose();
        _cts = null;
    }

    public async Task BroadcastAsync(EventDto evt)
    {
        var line = JsonSerializer.Serialize(evt, _jsonOptions) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        var dead = new List<string>();

        foreach (var (id, entry) in _clients)
        {
            try
            {
                await entry.Stream.WriteAsync(bytes).ConfigureAwait(false);
                await entry.Stream.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                dead.Add(id);
            }
        }

        foreach (var id in dead)
        {
            if (_clients.TryRemove(id, out var e))
                e.Client.Dispose();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }

            var id = Guid.NewGuid().ToString("N");
            var stream = client.GetStream();
            _clients[id] = new ClientEntry(client, stream);

            var task = Task.Run(() => HandleClientAsync(id, client, stream, ct), CancellationToken.None);
            _clientTasks[id] = task;
        }
    }

    private async Task HandleClientAsync(
        string id, TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                CommandDto? cmd = null;
                try { cmd = JsonSerializer.Deserialize<CommandDto>(line, _jsonOptions); }
                catch (JsonException) { }

                if (cmd is null) continue;

                // Isolated try so a throwing subscriber does not disconnect the
                // client — only that invocation is lost, not the read loop.
                try { CommandReceived?.Invoke(cmd); }
                catch { }

                // Broadcast a log event so every console sees the command that
                // was received (includes the originating client IP address).
                _ = BroadcastAsync(new EventDto(
                    Timestamp: DateTimeOffset.Now,
                    Type: EventType.LogEvent,
                    JobName: cmd.JobName,
                    Message: $"CommandReceived: {cmd.Action} {cmd.JobName} from {endpoint}"));
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            if (_clients.TryRemove(id, out _))
                client.Dispose();
            _clientTasks.TryRemove(id, out _);
        }
    }
}
