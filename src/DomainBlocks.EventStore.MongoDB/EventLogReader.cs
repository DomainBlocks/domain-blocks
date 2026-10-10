using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// The reads that a subscription makes of the event log: the last position of the sequence it follows, and the events
/// of that sequence between two positions, for catching up.
/// </summary>
internal sealed class EventLogReader<TEvent>(
    IMongoCollection<BsonDocument> eventLog,
    IEventDecoder<TEvent, BsonValue, BsonValue> decoder)
    where TEvent : notnull
{
    /// <summary>
    /// Starts a causally consistent session that has seen everything up to the change stream's anchor, so that a
    /// majority read in it sees at least every event up to the anchor, and nothing falls between catch-up and live.
    /// See docs/unpublished/event-store-database-contract.md.
    /// </summary>
    public async Task<IClientSessionHandle> StartCatchUpSessionAsync(
        BsonTimestamp changeStreamOperationTime,
        CancellationToken cancellationToken)
    {
        var session = await eventLog.Database.Client
            .StartSessionAsync(new ClientSessionOptions { CausalConsistency = true }, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            session.AdvanceOperationTime(changeStreamOperationTime);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the last position of the sequence selected by the filter and ordered by the position field, or
    /// <see langword="null"/> if the sequence is empty.
    /// </summary>
    public async Task<long?> GetLastPositionAsync(
        IClientSessionHandle? session,
        FilterDefinition<BsonDocument> filter,
        string positionFieldName,
        CancellationToken cancellationToken)
    {
        var projection = Builders<BsonDocument>.Projection.Include(positionFieldName);
        var sort = Builders<BsonDocument>.Sort.Descending(positionFieldName);

        var find = session is null ? eventLog.Find(filter) : eventLog.Find(session, filter);

        var last = await find
            .Sort(sort)
            .Limit(1)
            .Project(projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return last?[positionFieldName].AsInt64;
    }

    /// <summary>
    /// Reads the events of the sequence selected by the filter and ordered by the position field, after one position
    /// and up to another, inclusive. Used for subscription catch-up, in the session that read the high-water mark, so
    /// that the snapshot includes it.
    /// </summary>
    public async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpAsync(
        IClientSessionHandle session,
        FilterDefinition<BsonDocument> filter,
        string positionFieldName,
        long afterExclusive,
        long highWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        filter &= Builders<BsonDocument>.Filter.Gt(positionFieldName, afterExclusive);
        filter &= Builders<BsonDocument>.Filter.Lte(positionFieldName, highWaterMark);

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
}