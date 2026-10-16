namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Reads one store's event log for an <see cref="EventLogTail{TSourceEvent}"/>. The tail creates a source each time it
/// starts, and disposes it when it stops.
/// </summary>
/// <typeparam name="TSourceEvent">An event as the store holds it, which is decoded only on request.</typeparam>
internal interface IEventLogSource<TSourceEvent> : IAsyncDisposable
{
    /// <summary>
    /// Reads the end of the log. The first <see cref="ReadNextAsync"/> reads the events after it.
    /// </summary>
    ValueTask StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="maxCount"/> events after the last one read, in log order. A read that fails leaves
    /// the position unchanged, so the next call reads the same events. The events are valid only until the next call.
    /// </summary>
    ValueTask<IReadOnlyList<TSourceEvent>> ReadNextAsync(int maxCount, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the signal that wakes the tail after an append.
    /// </summary>
    IAppendSignal CreateAppendSignal();

    /// <summary>
    /// Decides whether a failure of <see cref="StartAsync"/> or <see cref="ReadNextAsync"/> is worth retrying.
    /// </summary>
    bool IsTransient(Exception exception);
}