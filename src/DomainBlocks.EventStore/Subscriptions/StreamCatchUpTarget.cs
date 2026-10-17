using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// One stream, which a subscription to the stream follows by stream position.
/// </summary>
internal sealed class StreamCatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos>(TStreamId streamId) :
    ICatchUpTarget<TSourceEvent, TStreamId, TStreamPos, TLogPos, TStreamPos>
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct
{
    public ValueTask<TStreamPos?> ReadEndPositionAsync(
        IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> source,
        CancellationToken cancellationToken)
    {
        return source.ReadStreamEndPositionAsync(streamId, cancellationToken);
    }

    public IAsyncEnumerable<TSourceEvent> ReadAsync(
        IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> source,
        TStreamPos? fromExclusive,
        TStreamPos toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        return source.ReadStreamAsync(streamId, fromExclusive, toInclusive, filter, cancellationToken);
    }

    public bool Contains(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
    {
        return EqualityComparer<TStreamId>.Default.Equals(context.StreamId, streamId);
    }

    public TStreamPos PositionOf(in ReadEventContext<TStreamId, TStreamPos, TLogPos> context) => context.StreamPosition;
}