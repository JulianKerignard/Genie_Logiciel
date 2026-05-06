using EasySave.Infrastructure.Events;
using EasySave.Services;
using EasySave.Shared;

namespace EasySave.Tests.V2;

public class RemoteConsoleBroadcastBridgeTests
{
    [Fact]
    public async Task Publish_EventDto_OnBus_ForwardsToServerBroadcast()
    {
        await using var bus = new ChannelEventBus();
        var server = new FakeRemoteConsoleServer();

        var bridge = new RemoteConsoleBroadcastBridge(bus, server);
        bridge.Start();

        var evt = new EventDto(DateTimeOffset.Now, EventType.JobStarted, JobName: "remote-job");
        bus.Publish(evt);

        await server.Received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(server.Broadcasts);
        Assert.Equal("remote-job", server.Broadcasts[0].JobName);
        Assert.Equal(EventType.JobStarted, server.Broadcasts[0].Type);
    }

    [Fact]
    public async Task Bridge_DoesNotForwardOtherTypesPublishedOnTheBus()
    {
        await using var bus = new ChannelEventBus();
        var server = new FakeRemoteConsoleServer();
        var bridge = new RemoteConsoleBroadcastBridge(bus, server);
        bridge.Start();

        // A JobProgressDto published directly (not wrapped in EventDto) must
        // not reach the server — the bridge listens for EventDto only. This
        // protects the wire format from accidental leakage of internal types.
        bus.Publish(new JobProgressDto("j", JobStateEnum.Running, "f", 1, 1, 1, 1));

        // Give the consumer a chance to dispatch.
        await Task.Delay(150);

        Assert.Empty(server.Broadcasts);
    }

    private sealed class FakeRemoteConsoleServer : IRemoteConsoleServer
    {
        public readonly List<EventDto> Broadcasts = new();
        public readonly TaskCompletionSource Received = new();

        public event Func<CommandDto, Task>? CommandReceived;

        public Task BroadcastAsync(EventDto evt)
        {
            lock (Broadcasts)
            {
                Broadcasts.Add(evt);
                Received.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task StartAsync(int port, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        // Suppress CS0067 (event never used) — required by the interface even though tests don't fire it.
        private void Touch() => CommandReceived?.Invoke(default!);
    }
}
