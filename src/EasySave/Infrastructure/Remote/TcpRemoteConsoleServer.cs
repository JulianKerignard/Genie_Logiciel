using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EasyLog;
using EasySave.Services;
using EasySave.Shared;

namespace EasySave.Infrastructure.Remote;

public sealed class TcpRemoteConsoleServer : IRemoteConsoleServer
{
    private readonly IDailyLogger _logger;
    private readonly X509Certificate2? _tlsCert;
    private readonly ConcurrentDictionary<string, ClientEntry> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public event Func<CommandDto, Task>? CommandReceived;

    // tlsCert is the optional server certificate. When non-null, every
    // accepted client is upgraded to TLS 1.2/1.3 via SslStream before the
    // NDJSON framing kicks in. Pass null (default) for plain-TCP behaviour.
    public TcpRemoteConsoleServer(IDailyLogger logger, X509Certificate2? tlsCert = null)
    {
        _logger = logger;
        _tlsCert = tlsCert;
    }

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
            try
            {
                await entry.WriteLock.WaitAsync();
            }
            catch (ObjectDisposedException)
            {
                // HandleClientAsync raced us to RemoveClient and disposed
                // the lock — the entry is already gone, just skip it.
                dead.Add(key);
                continue;
            }
            try
            {
                await entry.Writer.WriteLineAsync(json);
                await entry.Writer.FlushAsync();
            }
            catch { dead.Add(key); }
            finally
            {
                // Race: HandleClientAsync's finally may have disposed the
                // semaphore between WaitAsync and here on a brutal client
                // disconnect. Swallow and let the dead-list path clean up.
                try { entry.WriteLock.Release(); }
                catch (ObjectDisposedException) { }
            }
        }

        foreach (var key in dead)
            RemoveClient(key);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var key = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
        Stream stream;
        SslStream? sslStream = null;

        // Upgrade to TLS before the framing layer starts reading. If the
        // handshake fails, drop the client without poisoning the listener
        // loop — the next AcceptTcpClientAsync iteration is unaffected.
        if (_tlsCert is not null)
        {
            sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                await sslStream.AuthenticateAsServerAsync(
                    _tlsCert,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                    checkCertificateRevocation: false).WaitAsync(ct);
            }
            catch (Exception ex)
            {
                // Common causes: a plain-TCP client hit a TLS-enabled port,
                // protocol mismatch (TLS 1.0/1.1), or a cert mismatch on the
                // wire. Trace so the operator has a diagnostic instead of a
                // silent drop; the listener loop is unaffected.
                System.Diagnostics.Trace.TraceWarning(
                    $"[TcpRemoteConsoleServer] TLS handshake failed for {key}: {ex.GetType().Name}: {ex.Message}");
                sslStream.Dispose();
                client.Close();
                return;
            }
            stream = sslStream;
        }
        else
        {
            stream = client.GetStream();
        }

        var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true)
        { AutoFlush = false };
        _clients[key] = new ClientEntry(client, writer, sslStream);

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
                try { line = await ReadLineBoundedAsync(reader, MaxLineLengthChars, ct); }
                catch (InvalidDataException) { break; }
                catch (OperationCanceledException) { break; }
                if (line is null) break;

                CommandDto? cmd;
                try { cmd = JsonSerializer.Deserialize<CommandDto>(line); }
                catch { continue; }
                if (cmd is null) continue;

                cmd = cmd with { SourceIp = key };
                await FireCommandReceivedAsync(cmd);

                // Audit broadcast so a second console connected to the same
                // engine sees who issued the command. Must be awaited (not
                // fire-and-forget) so the CommandReceived frame is observed
                // before the orchestrator's resulting JobPaused/JobResumed
                // broadcast — otherwise clients can see the state change
                // before the command that caused it. HandleClientAsync is
                // already a detached task per AcceptTcpClientAsync, so
                // awaiting here does not block the accept loop.
                await BroadcastAsync(new EventDto(
                    Timestamp: DateTimeOffset.UtcNow,
                    Type: EventType.CommandReceived,
                    JobName: cmd.JobName,
                    Message: $"{cmd.SourceIp ?? "?"} → {cmd.Action}"));
            }
        }
        catch { /* network disconnect */ }
        finally { RemoveClient(key); }
    }

    // Cap is in characters (JSON commands are ASCII-only, so chars == bytes in practice).
    private const int MaxLineLengthChars = 64 * 1024;

    // Reads one line from reader, capping at max characters. Returns null on EOF.
    // Throws InvalidDataException when a line exceeds the cap so the caller
    // can drop the misbehaving client without buffering the full payload.
    private static async Task<string?> ReadLineBoundedAsync(StreamReader reader, int max, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new char[1];
        while (!ct.IsCancellationRequested)
        {
            int n = await reader.ReadAsync(buf.AsMemory(), ct);
            if (n == 0) return sb.Length == 0 ? null : sb.ToString();
            if (buf[0] == '\n') return sb.ToString();
            if (buf[0] == '\r') continue;
            if (sb.Length >= max)
                throw new InvalidDataException($"Line over {max} chars — client dropped");
            sb.Append(buf[0]);
        }
        return null;
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

        try { entry.WriteLock.Dispose(); } catch { }
        try { entry.Writer.Dispose(); } catch { }
        try { entry.SslStream?.Dispose(); } catch { }
        try { entry.Client.Close(); } catch { }
    }

    private sealed class ClientEntry(TcpClient client, StreamWriter writer, SslStream? sslStream)
    {
        public TcpClient Client { get; } = client;
        public StreamWriter Writer { get; } = writer;
        public SslStream? SslStream { get; } = sslStream;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }
}
