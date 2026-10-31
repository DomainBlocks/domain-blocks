using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Represents the reads of the event log's history, or of one stream's. The end position is read in a session that
/// waits for the change stream's high-water mark.
/// </summary>
/// <param name="eventLog">The event log.</param>
/// <param name="decoder">The decoder of the events that it reads.</param>
/// <param name="sequenceFilter">Selects the followed sequence, which sets its end position.</param>
/// <param name="replayFilter">Selects the events to replay from the followed sequence.</param>
/// <param name="positionFieldName">The name of the field that holds an event's position in the sequence.</param>
/// <param name="options">The subscription's options, which set how long a read keeps retrying.</param>
/// <param name="correlationId">The subscription's ID, which the log of each retry includes.</param>
/// <param name="logger">The logger for retries.</param>
internal sealed class MongoCatchUpSnapshotReader<TEvent, TPos>(
    IMongoCollection<BsonDocument> eventLog,
    IEventDecoder<TEvent, BsonValue, BsonValue> decoder,
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
        using var session = await StartSessionAsync(highWaterMark, cancellationToken).ConfigureAwait(false);

        var end = await ReadEndAsync(session, cancellationToken).ConfigureAwait(false);

        return new Snapshot(this, origin, end, session.OperationTime);
    }

    // Starts a causally consistent session whose reads wait until they can see every change up to the given time.
    private async Task<IClientSessionHandle> StartSessionAsync(
        BsonTimestamp operationTime,
        CancellationToken cancellationToken)
    {
        var session = await eventLog.Database.Client
            .StartSessionAsync(new ClientSessionOptions { CausalConsistency = true }, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            session.AdvanceOperationTime(operationTime);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private async Task<long?> ReadEndAsync(IClientSessionHandle session, CancellationToken cancellationToken)
    {
        var last = await eventLog
            .Find(session, sequenceFilter)
            .Sort(Builders<BsonDocument>.Sort.Descending(positionFieldName))
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include(positionFieldName))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return last?[positionFieldName].AsInt64;
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadEventsAsync(
        BsonTimestamp operationTime,
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var session = await StartSessionAsync(operationTime, cancellationToken).ConfigureAwait(false);

        var filter = replayFilter &
                     Builders<BsonDocument>.Filter.Gt(positionFieldName, startExclusive) &
                     Builders<BsonDocument>.Filter.Lte(positionFieldName, endInclusive);

        using var cursor = await eventLog
            .Find(session, filter)
            .Sort(Builders<BsonDocument>.Sort.Ascending(positionFieldName))
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
                yield return decoder.Decode(doc);
        }
    }

    /// <summary>
    /// Represents the sequence up to the end. Each read is in its own session, which waits for the operation time of
    /// the read of the end.
    /// </summary>
    private sealed class Snapshot(
        MongoCatchUpSnapshotReader<TEvent, TPos> owner,
        SubscriptionOrigin<TPos> origin,
        long? end,
        BsonTimestamp operationTime) :
        CatchUpSnapshot<TEvent, TPos>(origin, end, owner.RetryPolicy)
    {
        protected override IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return owner.ReadEventsAsync(operationTime, startExclusive, endInclusive, cancellationToken);
        }
    }
}