using EasySave.Shared;

namespace EasySave.Tests.V2;

public class ChannelEventBusTests
{
    [Fact]
    public async Task Subscribe_Then_Publish_DeliversEvent()
    {
        await using var bus = new ChannelEventBus();
        var received = new TaskCompletionSource<EventDto>();
        bus.Subscribe<EventDto>(e => received.TrySetResult(e));

        var sent = new EventDto(DateTimeOffset.Now, EventType.JobStarted, JobName: "J1");
        bus.Publish(sent);

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("J1", got.JobName);
        Assert.Equal(EventType.JobStarted, got.Type);
    }

    [Fact]
    public async Task Publish_DispatchesToAllSubscribersOfSameType()
    {
        await using var bus = new ChannelEventBus();
        var hits = 0;
        var done = new TaskCompletionSource<bool>();

        bus.Subscribe<EventDto>(_ => Interlocked.Increment(ref hits));
        bus.Subscribe<EventDto>(_ =>
        {
            if (Interlocked.Increment(ref hits) == 2) done.TrySetResult(true);
        });

        bus.Publish(new EventDto(DateTimeOffset.Now, EventType.JobProgress));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, hits);
    }

    [Fact]
    public async Task FaultedHandler_DoesNotPreventOtherHandlers()
    {
        // A bad subscriber must not stop other subscribers from getting the
        // event nor kill the consumer loop — a hard requirement for an
        // infrastructure bus shared across N components.
        await using var bus = new ChannelEventBus();
        var goodReceived = new TaskCompletionSource<bool>();
        bus.Subscribe<EventDto>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<EventDto>(_ => goodReceived.TrySetResult(true));

        bus.Publish(new EventDto(DateTimeOffset.Now, EventType.JobProgress));

        Assert.True(await goodReceived.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        // Loop is still alive — second event also delivered.
        var second = new TaskCompletionSource<bool>();
        bus.Subscribe<EventDto>(_ => second.TrySetResult(true));
        bus.Publish(new EventDto(DateTimeOffset.Now, EventType.JobFinished));
        Assert.True(await second.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Publish_OnlyDispatchesToHandlersOfMatchingType()
    {
        await using var bus = new ChannelEventBus();
        var eventDtoHits = 0;
        var stringHits = 0;
        var stringDone = new TaskCompletionSource<bool>();

        bus.Subscribe<EventDto>(_ => Interlocked.Increment(ref eventDtoHits));
        bus.Subscribe<string>(_ =>
        {
            Interlocked.Increment(ref stringHits);
            stringDone.TrySetResult(true);
        });

        bus.Publish("hello");

        await stringDone.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, eventDtoHits);
        Assert.Equal(1, stringHits);
    }

    [Fact]
    public void Publish_IsNonBlocking()
    {
        // The engine's file-copy loop must not be slowed by a slow consumer.
        // Even with a handler that sleeps for 200 ms, Publish must return in
        // milliseconds — work is offloaded to the consumer task.
        using var bus = new ChannelEventBus();
        bus.Subscribe<EventDto>(_ => Thread.Sleep(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
            bus.Publish(new EventDto(DateTimeOffset.Now, EventType.JobProgress));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"Publish blocked the caller (took {sw.ElapsedMilliseconds} ms for 50 events with a 200 ms handler).");
    }

    [Fact]
    public void Publish_NullEvent_Throws()
    {
        using var bus = new ChannelEventBus();
        Assert.Throws<ArgumentNullException>(() => bus.Publish<EventDto>(null!));
    }

    [Fact]
    public void Subscribe_NullHandler_Throws()
    {
        using var bus = new ChannelEventBus();
        Assert.Throws<ArgumentNullException>(() => bus.Subscribe<EventDto>(null!));
    }

    [Fact]
    public async Task Publish_AfterDispose_DoesNotThrow()
    {
        // Component shutdown order is not always controllable — a stray
        // Publish after DisposeAsync must drop silently, not crash the host.
        var bus = new ChannelEventBus();
        await bus.DisposeAsync();
        bus.Publish(new EventDto(DateTimeOffset.Now, EventType.JobProgress));
    }
}
