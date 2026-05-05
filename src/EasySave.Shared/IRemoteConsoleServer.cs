namespace EasySave.Shared;

// Server-side contract for the v3 TCP remote console channel. Implemented by
// TcpRemoteConsoleServer (in EasySave) and consumed by the composition root
// in EasySave.UI. Supports N simultaneous remote console clients.
public interface IRemoteConsoleServer
{
    // Starts the TCP listener on the given port. Returns once the listener is
    // bound; the accept loop runs in the background until ct is cancelled.
    Task StartAsync(int port, CancellationToken ct);

    // Stops the listener, closes all open client connections, and awaits the
    // accept loop to terminate cleanly.
    Task StopAsync();

    // Serialises evt to JSON and writes it as a single newline-terminated line
    // to every connected client's stream. Silently drops clients that have
    // disconnected since the last broadcast.
    Task BroadcastAsync(EventDto evt);

    // Raised on the thread-pool task that reads from an individual client.
    // Subscribers must be thread-safe; the event may fire from multiple client
    // reader tasks concurrently.
    event Action<CommandDto>? CommandReceived;
}
