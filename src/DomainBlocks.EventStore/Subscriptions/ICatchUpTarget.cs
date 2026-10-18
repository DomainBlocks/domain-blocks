using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// What a subscription follows, the event log or one stream, with the reads of a source that replay it, in the target's
/// own kind of position.
/// </summary>
internal interface ICatchUpTarget<TEvent, TStreamId, TStreamPos, TLogPos, TPos>
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct
    where TPos : struct
{
    /// <summary>
    /// Reads the position of the target's last event, or <see langword="null"/> if it has none.
    /// </summary>
    ValueTask<TPos?> ReadEndPositionAsync(
        IEventSubscriptionSource<TEvent, TStreamId, TStreamPos, TLogPos> source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the target's events that <paramref name="filter"/> selects, in order, after
    /// <paramref name="fromExclusive"/>, or from the start if it is <see langword="null"/>, up to and including
    /// <paramref name="toInclusive"/>.
    /// </summary>
    IAsyncEnumerable<TEvent> ReadAsync(
        IEventSubscriptionSource<TEvent, TStreamId, TStreamPos, TLogPos> source,
        TPos? fromExclusive,
        TPos toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether an event is in the target.
    /// </summary>
    bool Contains(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context);

    /// <summary>
    /// Gets an event's position in the target.
    /// </summary>
    TPos GetPosition(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context);
}