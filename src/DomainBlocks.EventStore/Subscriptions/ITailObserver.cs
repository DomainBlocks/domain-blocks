namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Receives the events that an <see cref="EventLogTail{TEvent,TSourceEvent,TStreamId,TStreamPos,TLogPos}"/> reads. The
/// tail calls it from its read loop, so it must not block.
/// </summary>
internal interface ITailObserver<in TEvent>
{
    /// <summary>
    /// Receives the next event, which is valid only during this call. An observer that throws is detached.
    /// </summary>
    void OnNext(TEvent @event);

    /// <summary>
    /// Receives the error that stopped the tail. Nothing follows it.
    /// </summary>
    void OnError(Exception error);
}