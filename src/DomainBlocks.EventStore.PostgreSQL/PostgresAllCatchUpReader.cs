using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Represents the reads of the event log's history. The end position is read behind the replication barrier, once
/// queries can see the feed's high-water mark.
/// </summary>
internal sealed class PostgresAllCatchUpReader<TEvent>(
    EventLogReader<TEvent> reader,
    EventFilter filter,
    SubscriptionOptions options,
    string correlationId,
    ILogger? logger) :
    CatchUpReader<TEvent, LogPosition, long>(
        PostgresTransientErrors.IsTransient,
        options,
        correlationId,
        logger)
    where TEvent : notnull
{
    protected override async ValueTask<CatchUp<TEvent, LogPosition>> ReadCoreAsync(
        SubscriptionOrigin<LogPosition> origin,
        long highWaterMark,
        CancellationToken cancellationToken)
    {
        await CatchUpVisibility
            .WaitAsync(reader, highWaterMark, CorrelationId, Logger, cancellationToken)
            .ConfigureAwait(false);

        // The subscription replays up to the end, or starts after it, so the read is followed by the replication
        // barrier.
        var end = await reader.ReadMaxPositionWithBarrierAsync(cancellationToken).ConfigureAwait(false);

        return new AllCatchUp(origin, end, ResiliencePipeline, reader, filter);
    }

    /// <summary>
    /// Represents the event log up to the end. The end bounds each read, since queries could already see it.
    /// </summary>
    private sealed class AllCatchUp(
        SubscriptionOrigin<LogPosition> origin,
        long? end,
        ResiliencePipeline resiliencePipeline,
        EventLogReader<TEvent> reader,
        EventFilter filter) :
        CatchUp<TEvent, LogPosition>(origin, end, resiliencePipeline)
    {
        protected override IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return reader.ReadCatchUpAllAsync(startExclusive, endInclusive, filter, cancellationToken);
        }
    }
}