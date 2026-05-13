using EasySave.Shared;

namespace EasySave.RemoteConsole.Abstractions;

/// <summary>Represents the TCP connection state of the remote console client.</summary>
public enum RemoteConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

/// <summary>
/// Client-side contract for the v3 TCP remote console protocol.
/// Connects to the EasySave server, receives job events, and sends control commands.
/// </summary>
public interface IRemoteConsoleClient
{
    /// <summary>
    /// When true, the next <see cref="ConnectAsync"/> upgrades the socket to
    /// TLS via <c>SslStream</c> and verifies the server certificate against
    /// the trust-on-first-use known_hosts store. Off by default — must be
    /// turned on by the operator before connecting if the engine has TLS
    /// enabled on its side.
    /// </summary>
    bool UseTls { get; set; }

    /// <summary>
    /// Establishes a TCP connection to the EasySave server at
    /// <paramref name="host"/>:<paramref name="port"/>.
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken ct);

    /// <summary>Gracefully closes the connection to the server.</summary>
    Task DisconnectAsync();

    /// <summary>Serialises <paramref name="cmd"/> and sends it to the server.</summary>
    Task SendCommandAsync(CommandDto cmd);

    /// <summary>
    /// Raised whenever the server pushes an event to this client.
    /// The implementation must await each handler and isolate per-handler exceptions so that
    /// one faulted subscriber does not prevent the others from running.
    /// </summary>
    event Func<EventDto, Task> EventReceived;

    /// <summary>
    /// Observable stream of connection state transitions.
    /// Uses <see cref="IObservable{T}"/> from <c>System</c> — no external Rx dependency required.
    /// </summary>
    IObservable<RemoteConnectionState> ConnectionState { get; }
}
