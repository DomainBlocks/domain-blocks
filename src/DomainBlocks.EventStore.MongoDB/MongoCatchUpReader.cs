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
internal sealed class MongoCatchUpReader<TEvent, TPos>(
    IMongoCollection<BsonDocument> eventLog,
    IEventDecoder<TEvent, BsonValue, BsonValue> decoder,
    FilterDefinition<BsonDocument> sequenceFilter,
    FilterDefinition<BsonDocument> replayFilter,
    string positionFieldName,
    SubscriptionOptions options,
    string correlationId,
    ILogger? logger) :
    CatchUpReader<TEvent, TPos, BsonTimestamp>(MongoTransientErrors.IsTransient, options, correlationId, logger)
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    // The session is causally consistent, so its first read waits until it can see every change up to the high-water
    // mark. That read advances the session's operation time past the end it found, so the session's later reads can't
    // see less than that end.
    protected override async ValueTask<CatchUp<TEvent, TPos>> ReadCoreAsync(
        SubscriptionOrigin<TPos> origin,
        BsonTimestamp highWaterMark,
        CancellationToken cancellationToken)
    {
        var session = await eventLog.Database.Client
            .StartSessionAsync(new ClientSessionOptions { CausalConsistency = true }, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            session.AdvanceOperationTime(highWaterMark);

            var end = await ReadEndAsync(session, cancellationToken).ConfigureAwait(false);

            return new MongoCatchUp(this, origin, end, session);
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
        IClientSessionHandle session,
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
    /// Represents the sequence up to the end. It holds the session that read the end, so each read of the events waits
    /// until it can see at least that end.
    /// </summary>
    private sealed class MongoCatchUp(
        MongoCatchUpReader<TEvent, TPos> owner,
        SubscriptionOrigin<TPos> origin,
        long? end,
        IClientSessionHandle session) :
        CatchUp<TEvent, TPos>(origin, end, owner.ResiliencePipeline)
    {
        protected override IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return owner.ReadEventsAsync(session, startExclusive, endInclusive, cancellationToken);
        }

        protected override ValueTask DisposeAsyncCore()
        {
            session.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}