using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Represents the reads of the event log's history, or of one stream's. The end position is read in a session that
/// waits for the change stream's high-water mark.
/// </summary>
/// <param name="reader">The reader of the event log.</param>
/// <param name="sequenceFilter">Selects the followed sequence, which sets its end position.</param>
/// <param name="replayFilter">Selects the events to replay from the followed sequence.</param>
/// <param name="positionFieldName">The name of the field that holds an event's position in the sequence.</param>
/// <param name="options">The subscription's options, which set how long a read keeps retrying.</param>
/// <param name="correlationId">The subscription's ID, which the log of each retry includes.</param>
/// <param name="logger">The logger for retries.</param>
internal sealed class MongoCatchUpSnapshotReader<TEvent, TPos>(
    EventLogReader<TEvent> reader,
    FilterDefinition<BsonDocument> sequenceFilter,
    FilterDefinition<BsonDocument> replayFilter,
    string positionFieldName,
    SubscriptionOptions options,
    string correlationId,
    ILogger? logger) :
    CatchUpSnapshotReader<TEvent, TPos, BsonTimestamp>(MongoTransientErrors.IsTransient, options, correlationId, logger)
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    // The read advances the session's operation time past the end it found, so a read of the snapshot that waits for
    // that time can't see less than that end.
    protected override async ValueTask<CatchUpSnapshot<TEvent, TPos>> ReadSnapshotCoreAsync(
        SubscriptionOrigin<TPos> origin,
        BsonTimestamp highWaterMark,
        CancellationToken cancellationToken)
    {
        using var session = await reader
            .StartCatchUpSessionAsync(highWaterMark, cancellationToken)
            .ConfigureAwait(false);

        var end = await reader
            .GetEndPositionAsync(session, sequenceFilter, positionFieldName, cancellationToken)
            .ConfigureAwait(false);

        return new Snapshot(
            origin,
            end,
            RetryPolicy,
            session.OperationTime,
            reader,
            replayFilter,
            positionFieldName);
    }

    /// <summary>
    /// Represents the sequence up to the end. Each read is in its own session, which waits for the operation time of
    /// the read of the end.
    /// </summary>
    private sealed class Snapshot(
        SubscriptionOrigin<TPos> origin,
        long? end,
        ResiliencePipeline resiliencePipeline,
        BsonTimestamp operationTime,
        EventLogReader<TEvent> reader,
        FilterDefinition<BsonDocument> replayFilter,
        string positionFieldName) :
        CatchUpSnapshot<TEvent, TPos>(origin, end, resiliencePipeline)
    {
        protected override async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var session = await reader
                .StartCatchUpSessionAsync(operationTime, cancellationToken)
                .ConfigureAwait(false);

            var events = reader.ReadCatchUpAsync(
                session,
                replayFilter,
                positionFieldName,
                startExclusive,
                endInclusive,
                cancellationToken);

            await foreach (var e in events.ConfigureAwait(false))
                yield return e;
        }
    }
}