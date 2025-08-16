using DomainBlocks.Persistence.Events.Abstractions;
using DomainBlocks.Persistence.Events.Abstractions.Exceptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Persistence.Events.MongoDB;

public class MongoEventDataStore(IMongoCollection<BsonDocument> collection) : IEventDataStore<BsonDocument>
{
    private static readonly InsertManyOptions InsertManyOptions = new() { IsOrdered = true };

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<EventData<BsonDocument>> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var currentStreamVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken);
        expectedVersion ??= currentStreamVersion;

        if (expectedVersion.Value != currentStreamVersion)
            throw new StreamConcurrencyException(streamId, expectedVersion.Value, currentStreamVersion);

        var committedAt = DateTime.UtcNow;

        var documents = events.Select((@event, index) =>
        {
            var streamVersion = currentStreamVersion + 1 + index;
            return ToDocument(streamId, streamVersion, @event, committedAt);
        });

        try
        {
            await collection.InsertManyAsync(documents, InsertManyOptions, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new StreamConcurrencyException(streamId, expectedVersion.Value, currentStreamVersion, ex);
        }
    }

    public async Task<ReadStreamResult<BsonDocument>> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("header.streamId", streamName);

        if (fromVersion.HasValue)
        {
            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<BsonDocument>.Filter.Gte("header.streamVersion", fromVersion.Value)
                : Builders<BsonDocument>.Filter.Lte("header.streamVersion", fromVersion.Value);

            filter = Builders<BsonDocument>.Filter.And(filter, versionFilter);
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<BsonDocument>.Sort.Ascending("header.streamVersion")
            : Builders<BsonDocument>.Sort.Descending("header.streamVersion");

        var cursor = await collection
            .Find(filter)
            .Sort(sort)
            .ToCursorAsync(cancellationToken);

        if (!await cursor.MoveNextAsync(cancellationToken) || !cursor.Current.Any())
            return ReadStreamResult<BsonDocument>.NotFound();

        return ReadStreamResult<BsonDocument>.Success(Enumerate());

        async IAsyncEnumerable<StoredEventData<BsonDocument>> Enumerate()
        {
            do
            {
                foreach (var doc in cursor.Current)
                {
                    var metadata = doc
                        .GetValueByPath("header.metadata")
                        .AsBsonDocument
                        .ToDictionary(x => x.Name, x => x.Value.AsString);

                    var @event = new StoredEventData<BsonDocument>(
                        doc.GetValueByPath("header.streamId").AsString,
                        doc.GetValueByPath("header.streamVersion").AsInt64,
                        doc.GetValueByPath("header.eventName").AsString,
                        doc.GetValueByPath("payload").AsBsonDocument,
                        metadata,
                        doc.GetValueByPath("header.committedAt").AsUniversalTime);

                    yield return @event;
                }
            } while (await cursor.MoveNextAsync(cancellationToken));

            // TODO: Improve this to guarantee dispose is called.
            cursor.Dispose();
        }
    }

    private static BsonDocument ToDocument(
        string streamId,
        long streamVersion,
        EventData<BsonDocument> @event,
        DateTime committedAt)
    {
        var doc = new BsonDocument();
        doc.SetValueByPath("header.streamId", streamId);
        doc.SetValueByPath("header.streamVersion", streamVersion);
        doc.SetValueByPath("header.eventName", @event.EventName);
        doc.SetValueByPath("payload", @event.Payload);
        doc.SetValueByPath("header.metadata", @event.Metadata.ToBsonDocument());
        doc.SetValueByPath("header.committedAt", committedAt);

        return doc;
    }

    private async Task<long> GetCurrentStreamVersionAsync(string streamId, CancellationToken cancellationToken)
    {
        var head = await collection
            .Find(Builders<BsonDocument>.Filter.Eq("header.streamId", streamId))
            .Sort(Builders<BsonDocument>.Sort.Descending("header.streamVersion"))
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);

        return head == null ? -1 : head.GetValueByPath("header.streamVersion").AsInt64;
    }
}