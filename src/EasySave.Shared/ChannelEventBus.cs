using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EasySave.Shared;

/// <summary>
/// In-memory <see cref="IEventBus"/> backed by a single unbounded
/// <see cref="Channel{T}"/>. Publish enqueues; one consumer task reads the
/// queue and dispatches to every registered handler for the event's runtime
/// type.
/// </summary>
/// <remarks>
/// <para>
/// Single-consumer design (vs. one channel per type) keeps event ordering
/// across types deterministic — a JobStarted published before a JobProgress
/// is delivered before the JobProgress, regardless of registration order.
/// </para>
/// <para>
/// <see cref="Publish{T}"/> is non-blocking by design: the writer is
/// unbounded and <c>TryWrite</c> never blocks on a hot path. Handlers run on
/// a single background task and a faulted handler is caught and ignored so
/// one bad subscriber cannot stop the others or kill the consumer loop.
/// </para>
/// </remarks>
public sealed class ChannelEventBus : IEventBus, IAsyncDisposable, IDisposable
{
    private readonly Channel<(Type Type, object Evt)> _channel =
        Channel.CreateUnbounded<(Type, object)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumer;
    private bool _disposed;

    public ChannelEventBus()
    {
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <inheritdoc />
    public void Publish<T>(T evt) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (_disposed) return;
        // TryWrite on an unbounded channel only fails when the writer is
        // completed (post-Dispose), in which case dropping the event is the
        // correct behaviour.
        _channel.Writer.TryWrite((typeof(T), evt));
    }

    /// <inheritdoc />
    public void Subscribe<T>(Action<T> handler) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        var list = _handlers.GetOrAdd(typeof(T), static _ => new List<Delegate>());
        lock (list) list.Add(handler);
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var (type, evt) in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                if (!_handlers.TryGetValue(type, out var list)) continue;

                Delegate[] snapshot;
                lock (list) snapshot = list.ToArray();

                foreach (var d in snapshot)
                {
                    try { d.DynamicInvoke(evt); }
                    catch
                    {
                        // Isolate per-handler exceptions: one faulted subscriber
                        // must not stop other subscribers, nor kill the consumer
                        // loop. Logging is intentionally absent here so the bus
                        // stays infrastructure-light; a hosted logger subscriber
                        // would re-publish the failure if needed.
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try { await _consumer.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    /// <summary>
    /// Synchronous shutdown for callers in non-async contexts (test
    /// teardown, console hosts). Delegates to <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
