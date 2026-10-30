using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Subscriptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Represents the reads of the event log's history, or of one stream's. Each read is in its own session, which waits
/// for the operation time that it is given as the mark.
/// </summary>
/// <param name="reader">The reader of the event log.</param>
/// <param name="sequenceFilter">Selects the followed sequence, which sets its end position.</param>
/// <param name="replayFilter">Selects the events to replay from the followed sequence.</param>
/// <param name="positionFieldName">The name of the field that holds an event's position in the sequence.</param>
internal sealed class MongoCatchUpReader<TEvent>(
    EventLogReader<TEvent> reader,
    FilterDefinition<BsonDocument> sequenceFilter,
    FilterDefinition<BsonDocument> replayFilter,
    string positionFieldName) :
    ICatchUpReader<TEvent, BsonTimestamp>
    where TEvent : notnull
{
    // The read advances the session's operation time past the end it found, so a replay that waits for that time can't
    // see less than that end.
    public async ValueTask<(long? End, BsonTimestamp Mark)> ReadEndAsync(
        BsonTimestamp mark,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var session = await reader.StartCatchUpSessionAsync(mark, cancellationToken).ConfigureAwait(false);

        var end = await reader
            .GetEndPositionAsync(session, sequenceFilter, positionFieldName, cancellationToken)
            .ConfigureAwait(false);

        return (end, session.OperationTime);
    }

    public async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        BsonTimestamp mark,
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var session = await reader.StartCatchUpSessionAsync(mark, cancellationToken).ConfigureAwait(false);

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