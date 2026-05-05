using EasySave.Shared;

namespace EasySave.Services;

/// <summary>
/// Exposes the v3 TCP server that streams job events to connected remote consoles
/// and receives commands from them.
/// </summary>
/// <remarks>Implementations must support N simultaneous client connections.</remarks>
public interface IRemoteConsoleServer
{
    /// <summary>
    /// Starts the TCP listener on <paramref name="port"/> and begins accepting client
    /// connections until <paramref name="ct"/> is cancelled.
    /// </summary>
    Task StartAsync(int port, CancellationToken ct);

    /// <summary>Stops the listener and closes all active client connections.</summary>
    Task StopAsync();

    /// <summary>Serialises <paramref name="evt"/> and sends it to every connected client.</summary>
    Task BroadcastAsync(EventDto evt);

    /// <summary>
    /// Raised whenever a connected client sends a command.
    /// The implementation must await each handler and isolate per-handler exceptions so that
    /// one faulted subscriber does not prevent the others from running.
    /// </summary>
    event Func<CommandDto, Task> CommandReceived;
}
