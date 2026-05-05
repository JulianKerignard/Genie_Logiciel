using EasySave.Shared;

namespace EasySave.RemoteConsole.Infrastructure;

// Client-side contract for the v3 TCP remote console channel.
// ConnectAsync is non-blocking: it triggers a background connect-and-read loop
// that auto-reconnects with exponential back-off on network failures.
public interface IRemoteConsoleClient
{
    // Initiates the connection (and auto-reconnect loop) in the background.
    Task ConnectAsync(string host, int port, CancellationToken ct);

    // Cancels the background loop and closes the socket.
    Task DisconnectAsync();

    // Serialises cmd to JSON and sends it over the open socket.
    // No-op when the socket is not connected.
    Task SendCommandAsync(CommandDto cmd);

    // Raised on the thread-pool task that reads from the server socket.
    // Subscribers must be thread-safe.
    event Action<EventDto>? EventReceived;

    // Hot observable that emits the current ConnectionState immediately on
    // subscribe, then every time the state changes. Never completes.
    IObservable<ConnectionState> ConnectionState { get; }
}
