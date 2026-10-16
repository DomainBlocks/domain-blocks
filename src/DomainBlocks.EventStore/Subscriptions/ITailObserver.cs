namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Receives the events that an <see cref="EventLogTail{TSourceEvent}"/> reads. The tail calls it from its read loop,
/// so it must not block.
/// </summary>
internal interface ITailObserver<in TSourceEvent>
{
    /// <summary>
    /// Receives the next event, which is valid only during this call. An observer that throws is detached.
    /// </summary>
    void OnNext(TSourceEvent sourceEvent);

    /// <summary>
    /// Receives the error that stopped the tail. Nothing follows it.
    /// </summary>
    void OnError(Exception error);
}