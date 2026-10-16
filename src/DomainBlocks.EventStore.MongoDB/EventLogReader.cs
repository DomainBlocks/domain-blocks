using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Reads event positions and events from an event log for a subscription's catch-up process, allowing the subscription
/// to resume from a known position and read events up to a specified boundary before switching to live delivery.
/// </summary>
internal sealed class EventLogReader<TEvent>(
    IMongoCollection<BsonDocument> eventLog,
    IEventDecoder<TEvent, BsonValue, BsonValue> decoder)
    where TEvent : notnull
{
    /// <summary>
    /// Starts a causally consistent session whose optime is advanced to the change stream's anchor.
    /// </summary>
    public async Task<IClientSessionHandle> StartCatchUpSessionAsync(
        BsonTimestamp changeStreamOpTime,
        CancellationToken cancellationToken)
    {
        var session = await eventLog.Database.Client
            .StartSessionAsync(new ClientSessionOptions { CausalConsistency = true }, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            session.AdvanceOperationTime(changeStreamOpTime);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the end position in the sequence selected by the specified filter, or <see langword="null"/> if the
    /// sequence contains no events.
    /// </summary>
    public async Task<long?> GetEndPositionAsync(
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
    /// Reads events from the selected sequence whose positions are greater than the starting position and less than or
    /// equal to the ending position, in ascending position order.
    /// </summary>
    public async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCatchUpAsync(
        IClientSessionHandle session,
        FilterDefinition<BsonDocument> filter,
        string positionFieldName,
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        filter &= Builders<BsonDocument>.Filter.Gt(positionFieldName, startExclusive);
        filter &= Builders<BsonDocument>.Filter.Lte(positionFieldName, endInclusive);

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