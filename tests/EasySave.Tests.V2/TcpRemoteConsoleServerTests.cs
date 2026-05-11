using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EasyLog;
using EasySave.Infrastructure.Remote;
using EasySave.Shared;

namespace EasySave.Tests.V2;

public class TcpRemoteConsoleServerTests
{
    [Fact]
    public async Task SingleClient_ReceivesBroadcastEvent()
    {
        int port = GetFreePort();
        var server = new TcpRemoteConsoleServer(NullLogger.Instance);
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(port, serverCts.Token);

        await WaitForListenerAsync(port);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);

        // Server adds the client to its internal map asynchronously after
        // AcceptTcpClientAsync returns; give it a beat so the next Broadcast
        // sees the client.
        await Task.Delay(100);

        var evt = new EventDto(DateTimeOffset.Now, EventType.JobStarted, JobName: "single");
        await server.BroadcastAsync(evt);

        var line = await ReadLineAsync(reader, TimeSpan.FromSeconds(2));
        var got = JsonSerializer.Deserialize<EventDto>(line);

        Assert.NotNull(got);
        Assert.Equal("single", got!.JobName);
        Assert.Equal(EventType.JobStarted, got.Type);

        await server.StopAsync();
    }

    [Fact]
    public async Task ThreeClients_AllReceiveSameBroadcast()
    {
        int port = GetFreePort();
        var server = new TcpRemoteConsoleServer(NullLogger.Instance);
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(port, serverCts.Token);

        await WaitForListenerAsync(port);

        var clients = new List<TcpClient>();
        var readers = new List<StreamReader>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var c = new TcpClient();
                await c.ConnectAsync(IPAddress.Loopback, port);
                clients.Add(c);
                readers.Add(new StreamReader(c.GetStream(), Encoding.UTF8));
            }

            await Task.Delay(150); // give server time to register all 3 connections

            var evt = new EventDto(DateTimeOffset.Now, EventType.JobProgress, JobName: "fanout");
            await server.BroadcastAsync(evt);

            foreach (var r in readers)
            {
                var line = await ReadLineAsync(r, TimeSpan.FromSeconds(2));
                var got = JsonSerializer.Deserialize<EventDto>(line);
                Assert.Equal("fanout", got!.JobName);
                Assert.Equal(EventType.JobProgress, got.Type);
            }
        }
        finally
        {
            foreach (var r in readers) r.Dispose();
            foreach (var c in clients) c.Dispose();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ClientCommand_FiresCommandReceivedEvent_WithExpectedPayload()
    {
        int port = GetFreePort();
        var server = new TcpRemoteConsoleServer(NullLogger.Instance);
        var received = new TaskCompletionSource<CommandDto>();
        server.CommandReceived += cmd =>
        {
            received.TrySetResult(cmd);
            return Task.CompletedTask;
        };

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(port, serverCts.Token);
        await WaitForListenerAsync(port);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true };

        var cmd = new CommandDto(JobName: "Photos", Action: CommandType.Pause);
        await writer.WriteLineAsync(JsonSerializer.Serialize(cmd));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Photos", got.JobName);
        Assert.Equal(CommandType.Pause, got.Action);
        Assert.NotNull(got.SourceIp); // server stamps the remote endpoint after deserialization

        await server.StopAsync();
    }

    [Fact]
    public async Task BrutalDisconnect_RemovesClientFromInternalMap_NoCrashOnNextBroadcast()
    {
        // Two clients connect; one drops abruptly. The next BroadcastAsync
        // must complete without throwing and the dead client's stream must
        // be cleaned up (assertion: surviving client still gets the event).
        int port = GetFreePort();
        var server = new TcpRemoteConsoleServer(NullLogger.Instance);
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(port, serverCts.Token);
        await WaitForListenerAsync(port);

        var dead = new TcpClient();
        await dead.ConnectAsync(IPAddress.Loopback, port);

        var alive = new TcpClient();
        await alive.ConnectAsync(IPAddress.Loopback, port);
        using var aliveReader = new StreamReader(alive.GetStream(), Encoding.UTF8);

        await Task.Delay(150);

        // Brutal close — no FIN, just rip the socket.
        dead.Client.Close(0);
        dead.Dispose();

        // First broadcast may attempt a write to the dead socket (and
        // catch). The server is expected to mark it dead, and the alive
        // client still receives the event.
        var evt = new EventDto(DateTimeOffset.Now, EventType.JobFinished, JobName: "post-brutal");
        await server.BroadcastAsync(evt);

        var line = await ReadLineAsync(aliveReader, TimeSpan.FromSeconds(2));
        var got = JsonSerializer.Deserialize<EventDto>(line);
        Assert.Equal("post-brutal", got!.JobName);

        // Second broadcast — the dead entry should be gone now, and the
        // call must again succeed without surfacing any exception.
        var evt2 = new EventDto(DateTimeOffset.Now, EventType.JobFinished, JobName: "second");
        await server.BroadcastAsync(evt2);
        var line2 = await ReadLineAsync(aliveReader, TimeSpan.FromSeconds(2));
        Assert.Equal("second", JsonSerializer.Deserialize<EventDto>(line2)!.JobName);

        alive.Dispose();
        await server.StopAsync();
    }

    [Fact]
    public async Task ClientCommand_BroadcastsCommandReceivedEventToAllClients()
    {
        // Multi-console audit: when one console sends a command, the server
        // echoes a CommandReceived event to every connected client (sender
        // included) so a second console attached to the same engine sees
        // who issued what.
        int port = GetFreePort();
        var server = new TcpRemoteConsoleServer(NullLogger.Instance);
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(port, serverCts.Token);
        await WaitForListenerAsync(port);

        using var sender = new TcpClient();
        await sender.ConnectAsync(IPAddress.Loopback, port);
        using var senderReader = new StreamReader(sender.GetStream(), Encoding.UTF8);
        using var senderWriter = new StreamWriter(sender.GetStream(), Encoding.UTF8) { AutoFlush = true };

        using var observer = new TcpClient();
        await observer.ConnectAsync(IPAddress.Loopback, port);
        using var observerReader = new StreamReader(observer.GetStream(), Encoding.UTF8);

        await Task.Delay(150);

        var cmd = new CommandDto(JobName: "Docs", Action: CommandType.Pause);
        await senderWriter.WriteLineAsync(JsonSerializer.Serialize(cmd));

        // Both clients must receive the audit event. ReadLineAsync may
        // surface unrelated frames (none expected from this server in this
        // test, but be defensive) — pick the first CommandReceived line.
        var senderEvt = await ReadCommandReceivedAsync(senderReader, TimeSpan.FromSeconds(2));
        var observerEvt = await ReadCommandReceivedAsync(observerReader, TimeSpan.FromSeconds(2));

        Assert.Equal("Docs", senderEvt.JobName);
        Assert.Equal("Docs", observerEvt.JobName);
        Assert.NotNull(senderEvt.Message);
        Assert.NotNull(observerEvt.Message);
        // Message shape: "<sourceIp:port> → <Action>"
        Assert.Contains("→ Pause", senderEvt.Message);
        Assert.Contains("→ Pause", observerEvt.Message);
        // The sender's endpoint must show up in both audits.
        Assert.Equal(senderEvt.Message, observerEvt.Message);

        await server.StopAsync();
    }

    [Fact]
    public async Task StopAsync_ClosesAllConnectedClients()
    {
        int port = GetFreePort();
        var server = new TcpRemoteConsoleServer(NullLogger.Instance);
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = server.StartAsync(port, serverCts.Token);
        await WaitForListenerAsync(port);

        using var c1 = new TcpClient();
        await c1.ConnectAsync(IPAddress.Loopback, port);
        using var c2 = new TcpClient();
        await c2.ConnectAsync(IPAddress.Loopback, port);
        using var r1 = new StreamReader(c1.GetStream(), Encoding.UTF8);
        using var r2 = new StreamReader(c2.GetStream(), Encoding.UTF8);

        await Task.Delay(150);

        await server.StopAsync();

        // After Stop, the server-side stream is closed. ReadLineAsync on a
        // closed peer returns null (graceful EOF) — the contract for "all
        // clients closed".
        var line1 = await r1.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var line2 = await r2.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(line1);
        Assert.Null(line2);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static int GetFreePort()
    {
        // Bind once on port 0 so the OS picks a free ephemeral port, then
        // close immediately. Race window with another process grabbing the
        // same port between Stop and the server's Start exists but is
        // negligible on loopback.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task WaitForListenerAsync(int port, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(25);
            }
        }
        throw new TimeoutException($"Server listener on port {port} never came up.");
    }

    private static async Task<string> ReadLineAsync(StreamReader reader, TimeSpan timeout)
    {
        var task = reader.ReadLineAsync();
        var line = await task.WaitAsync(timeout);
        return line ?? throw new InvalidOperationException("Stream closed before a line arrived.");
    }

    // Drains the stream until a CommandReceived event arrives or the timeout
    // budget elapses. Skips unrelated event types so the test stays robust
    // if the server emits additional frames concurrently.
    private static async Task<EventDto> ReadCommandReceivedAsync(StreamReader reader, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var line = await ReadLineAsync(reader, remaining);
            var evt = JsonSerializer.Deserialize<EventDto>(line);
            if (evt is { Type: EventType.CommandReceived }) return evt;
        }
        throw new TimeoutException("No CommandReceived event arrived within the deadline.");
    }

    // No-op IDailyLogger so the server can record its connect/disconnect
    // events without touching the filesystem.
    private sealed class NullLogger : IDailyLogger
    {
        public static readonly NullLogger Instance = new();
        private NullLogger() { }
        public void Append(LogEntry entry) { }
    }
}
