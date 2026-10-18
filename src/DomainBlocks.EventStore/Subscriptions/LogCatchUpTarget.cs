using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// The event log, which a subscription to all events follows by log position.
/// </summary>
internal sealed class LogCatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos> :
    ICatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos, TLogPos>
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct
{
    public static readonly LogCatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos> Instance = new();

    private LogCatchUpTarget()
    {
    }

    public ValueTask<TLogPos?> ReadEndPositionAsync(
        IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> source,
        CancellationToken cancellationToken)
    {
        return source.ReadEndLogPositionAsync(cancellationToken);
    }

    public IAsyncEnumerable<TSourceEvent> ReadAsync(
        IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> source,
        TLogPos? fromExclusive,
        TLogPos toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        return source.ReadAllAsync(fromExclusive, toInclusive, filter, cancellationToken);
    }

    public bool Contains(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context) => true;

    public TLogPos PositionOf(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context) => context.LogPosition;
}