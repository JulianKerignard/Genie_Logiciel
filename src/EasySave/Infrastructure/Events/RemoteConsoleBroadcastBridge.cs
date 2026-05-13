using EasySave.Services;
using EasySave.Shared;

namespace EasySave.Infrastructure.Events;

/// <summary>
/// Subscribes to <see cref="EventDto"/> on the shared <see cref="IEventBus"/>
/// and forwards each one to <see cref="IRemoteConsoleServer.BroadcastAsync"/>.
/// The other end of the bridge from <see cref="StateTrackerEventBridge"/>:
/// engine publishes, server broadcasts, neither knows about the other.
/// </summary>
/// <remarks>
/// <para>
/// The handler is fire-and-forget: <c>BroadcastAsync</c> is awaited inside the
/// bus consumer task so a slow client cannot block the engine, but its
/// completion is intentionally not surfaced — broadcast failures are isolated
/// per client by <see cref="IRemoteConsoleServer"/> itself, and the bus
/// guarantees a faulted handler is caught.
/// </para>
/// <para>
/// Call <see cref="Start"/> once at app boot, after the bus and the server
/// are constructed.
/// </para>
/// </remarks>
public sealed class RemoteConsoleBroadcastBridge
{
    private readonly IEventBus _bus;
    private readonly IRemoteConsoleServer _server;
    private bool _started;

    public RemoteConsoleBroadcastBridge(IEventBus bus, IRemoteConsoleServer server)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(server);
        _bus = bus;
        _server = server;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _bus.Subscribe<EventDto>(OnEvent);
    }

    private void OnEvent(EventDto evt)
    {
        // Sync-over-async is intentional: the bus consumer task drives one
        // event at a time, so blocking it on a Task is fine and keeps event
        // ordering observable to clients. Per-client write lock and dead-
        // client cleanup are handled inside TcpRemoteConsoleServer.
        _server.BroadcastAsync(evt).GetAwaiter().GetResult();
    }
}
