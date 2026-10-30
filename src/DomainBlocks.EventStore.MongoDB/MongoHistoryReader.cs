using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Represents the reads of the event log's history, or of one stream's. The end position is read in a session that
/// waits for the change stream's high-water mark.
/// </summary>
/// <param name="reader">The reader of the event log.</param>
/// <param name="sequenceFilter">Selects the followed sequence, which sets its end position.</param>
/// <param name="replayFilter">Selects the events to replay from the followed sequence.</param>
/// <param name="positionFieldName">The name of the field that holds an event's position in the sequence.</param>
internal sealed class MongoHistoryReader<TEvent>(
    EventLogReader<TEvent> reader,
    FilterDefinition<BsonDocument> sequenceFilter,
    FilterDefinition<BsonDocument> replayFilter,
    string positionFieldName) :
    IHistoryReader<TEvent, BsonTimestamp>
    where TEvent : notnull
{
    // The read advances the session's operation time past the end it found, so a read of the snapshot that waits for
    // that time can't see less than that end.
    public async ValueTask<IHistorySnapshot<TEvent>> ReadSnapshotAsync(
        BsonTimestamp highWaterMark,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var session = await reader
            .StartCatchUpSessionAsync(highWaterMark, cancellationToken)
            .ConfigureAwait(false);

        var end = await reader
            .GetEndPositionAsync(session, sequenceFilter, positionFieldName, cancellationToken)
            .ConfigureAwait(false);

        return new Snapshot(this, end, session.OperationTime);
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        BsonTimestamp operationTime,
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

    /// <summary>
    /// Represents the sequence up to the end. Each read is in its own session, which waits for the operation time of
    /// the read of the end.
    /// </summary>
    private sealed class Snapshot(MongoHistoryReader<TEvent> owner, long? end, BsonTimestamp operationTime) :
        IHistorySnapshot<TEvent>
    {
        public long? End => end;

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return owner.ReadAsync(operationTime, startExclusive, endInclusive, cancellationToken);
        }
    }
}