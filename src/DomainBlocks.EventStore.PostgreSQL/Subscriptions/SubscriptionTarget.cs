using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL.Subscriptions;

/// <summary>
/// What a subscription observes: the whole log or a single stream. Supplies the catch-up query, the live filter and
/// the position used to resume.
/// </summary>
internal abstract class SubscriptionTarget<TPos> where TPos : struct, IPosition<TPos>
{
    public abstract IAsyncEnumerable<EventLogRow> ReadCatchUpAsync(
        EventLogReader reader,
        long afterExclusive,
        long highWaterMark,
        CancellationToken cancellationToken);

    public abstract bool IsLiveMatch(EventLogRow row);

    public abstract TPos PositionOf(ReadEventContext<string, StreamPosition, LogPosition> context);
}

internal sealed class AllStreamsTarget : SubscriptionTarget<LogPosition>
{
    public static readonly AllStreamsTarget Instance = new();

    private AllStreamsTarget()
    {
    }

    public override IAsyncEnumerable<EventLogRow> ReadCatchUpAsync(
        EventLogReader reader,
        long afterExclusive,
        long highWaterMark,
        CancellationToken cancellationToken)
    {
        return reader.ReadCatchUpAllAsync(afterExclusive, highWaterMark, cancellationToken);
    }

    public override bool IsLiveMatch(EventLogRow row) => true;

    public override LogPosition PositionOf(ReadEventContext<string, StreamPosition, LogPosition> context) =>
        context.LogPosition;
}
