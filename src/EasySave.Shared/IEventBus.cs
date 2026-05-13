namespace EasySave.Shared;

/// <summary>
/// Minimal in-process pub/sub bus the V3 components use to broadcast engine
/// events without the publisher knowing the consumers. The engine publishes
/// <see cref="EventDto"/> snapshots; the remote console server, the daily
/// logger, the GUI, and any future consumer subscribe by event type.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Publish{T}"/> is non-blocking — the call returns immediately and
/// the work runs on a consumer task. A faulted handler must not stop other
/// handlers from receiving the event.
/// </para>
/// <para>
/// The bus is type-keyed: <see cref="Publish{T}"/> dispatches only to handlers
/// registered for the exact same <typeparamref name="T"/>. There is no
/// inheritance / hierarchy resolution.
/// </para>
/// </remarks>
public interface IEventBus
{
    /// <summary>
    /// Enqueues <paramref name="evt"/> for delivery. Returns immediately;
    /// handlers are invoked on a background consumer task.
    /// </summary>
    void Publish<T>(T evt) where T : notnull;

    /// <summary>
    /// Registers <paramref name="handler"/> to receive every <typeparamref name="T"/>
    /// published from this point on. Subscriptions are not removed automatically.
    /// </summary>
    void Subscribe<T>(Action<T> handler) where T : notnull;
}
