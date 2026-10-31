using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Represents the reads of one stream's history. The end position is read behind the replication barrier, once queries
/// can see the feed's high-water mark.
/// </summary>
internal sealed class PostgresStreamCatchUpReader<TEvent>(
    EventLogReader<TEvent> reader,
    string streamId,
    EventFilter filter,
    SubscriptionOptions options,
    string correlationId,
    ILogger? logger) :
    CatchUpReader<TEvent, StreamPosition, long>(
        PostgresTransientErrors.IsTransient,
        options,
        correlationId,
        logger)
    where TEvent : notnull
{
    protected override async ValueTask<CatchUp<TEvent, StreamPosition>> ReadCoreAsync(
        SubscriptionOrigin<StreamPosition> origin,
        long highWaterMark,
        CancellationToken cancellationToken)
    {
        await CatchUpVisibility
            .WaitAsync(reader, highWaterMark, CorrelationId, Logger, cancellationToken)
            .ConfigureAwait(false);

        // The subscription replays up to the end, or starts after it, so the read is followed by the replication
        // barrier.
        var end = await reader.ReadMaxStreamPositionWithBarrierAsync(streamId, cancellationToken).ConfigureAwait(false);

        return new StreamCatchUp(origin, end, ResiliencePipeline, reader, streamId, filter);
    }

    /// <summary>
    /// Represents the stream up to the end. The end bounds each read, since queries could already see it.
    /// </summary>
    private sealed class StreamCatchUp(
        SubscriptionOrigin<StreamPosition> origin,
        long? end,
        ResiliencePipeline resiliencePipeline,
        EventLogReader<TEvent> reader,
        string streamId,
        EventFilter filter) :
        CatchUp<TEvent, StreamPosition>(origin, end, resiliencePipeline)
    {
        protected override IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return reader.ReadCatchUpStreamAsync(streamId, startExclusive, endInclusive, filter, cancellationToken);
        }
    }
}