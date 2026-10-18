using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// The event log, which a subscription to all events follows by log position.
/// </summary>
internal sealed class LogCatchUpTarget<TEvent, TStreamId, TStreamPos, TLogPos> :
    ICatchUpTarget<TEvent, TStreamId, TStreamPos, TLogPos, TLogPos>
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct
{
    public static readonly LogCatchUpTarget<TEvent, TStreamId, TStreamPos, TLogPos> Instance = new();

    private LogCatchUpTarget()
    {
    }

    public ValueTask<TLogPos?> ReadEndPositionAsync(
        IEventSubscriptionSource<TEvent, TStreamId, TStreamPos, TLogPos> source,
        CancellationToken cancellationToken)
    {
        return source.ReadEndLogPositionAsync(cancellationToken);
    }

    public IAsyncEnumerable<TEvent> ReadAsync(
        IEventSubscriptionSource<TEvent, TStreamId, TStreamPos, TLogPos> source,
        TLogPos? fromExclusive,
        TLogPos toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        return source.ReadAllAsync(fromExclusive, toInclusive, filter, cancellationToken);
    }

    public bool Contains(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context) => true;

    public TLogPos GetPosition(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context) => context.LogPosition;
}