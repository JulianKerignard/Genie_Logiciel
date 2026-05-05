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
    /// Establishes a TCP connection to the EasySave server at
    /// <paramref name="host"/>:<paramref name="port"/>.
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken ct);

    /// <summary>Gracefully closes the connection to the server.</summary>
    Task DisconnectAsync();

    /// <summary>Serialises <paramref name="cmd"/> and sends it to the server.</summary>
    Task SendCommandAsync(CommandDto cmd);

    /// <summary>Raised on the thread-pool whenever the server pushes an event to this client.</summary>
    event Action<EventDto> EventReceived;

    /// <summary>
    /// Observable stream of connection state transitions.
    /// Uses <see cref="IObservable{T}"/> from <c>System</c> — no external Rx dependency required.
    /// </summary>
    IObservable<RemoteConnectionState> ConnectionState { get; }
}
