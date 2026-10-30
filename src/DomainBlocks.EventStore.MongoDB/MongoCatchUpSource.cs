using DomainBlocks.EventStore.Internal.Subscriptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Represents the history of the event log, or of one stream. A catch-up reads in a session that waits for the change
/// stream's high-water mark.
/// </summary>
/// <param name="reader">The reader of the event log.</param>
/// <param name="sequenceFilter">Selects the followed sequence, which sets its end position.</param>
/// <param name="replayFilter">Selects the events to replay from the followed sequence.</param>
/// <param name="positionFieldName">The name of the field that holds an event's position in the sequence.</param>
internal sealed class MongoCatchUpSource<TEvent>(
    EventLogReader<TEvent> reader,
    FilterDefinition<BsonDocument> sequenceFilter,
    FilterDefinition<BsonDocument> replayFilter,
    string positionFieldName) :
    ICatchUpSource<TEvent, BsonTimestamp>
    where TEvent : notnull
{
    public async ValueTask<ICatchUp<TEvent>> StartAsync(
        BsonTimestamp highWaterMark,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var session = await reader.StartCatchUpSessionAsync(highWaterMark, cancellationToken).ConfigureAwait(false);

        return new SessionCatchUp(this, session);
    }

    private Task<long?> ReadEndAsync(IClientSessionHandle session, CancellationToken cancellationToken)
    {
        return reader.GetEndPositionAsync(session, sequenceFilter, positionFieldName, cancellationToken);
    }

    private IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        IClientSessionHandle session,
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken)
    {
        return reader.ReadCatchUpAsync(
            session,
            replayFilter,
            positionFieldName,
            startExclusive,
            endInclusive,
            cancellationToken);
    }

    /// <summary>
    /// Represents the reads of one catch-up, in a session that waits for the high-water mark, so the end position and
    /// the replay include every event that the observer won't receive from the stream.
    /// </summary>
    private sealed class SessionCatchUp(MongoCatchUpSource<TEvent> source, IClientSessionHandle session) :
        ICatchUp<TEvent>
    {
        public ValueTask<long?> ReadEndAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<long?>(source.ReadEndAsync(session, cancellationToken));
        }

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return source.ReadAsync(session, startExclusive, endInclusive, cancellationToken);
        }

        public void Dispose() => session.Dispose();
    }
}