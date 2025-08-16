using DomainBlocks.Persistence.Abstractions.Events;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Persistence.Mongo.Events;

public class MongoEventDataStore<TPayload>(
    IMongoCollection<BsonDocument> collection,
    Func<TPayload, BsonValue> toBsonValue,
    Func<BsonValue, TPayload> toPayload) :
    IEventDataStore<TPayload>
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<EventData<TPayload>> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var currentStreamVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken);
        expectedVersion ??= currentStreamVersion;

        if (expectedVersion.Value != currentStreamVersion)
            throw new WrongExpectedVersionException(streamId, expectedVersion.Value, currentStreamVersion);

        var committedAt = DateTime.UtcNow;

        var documents = events.Select((@event, index) =>
        {
            var streamVersion = currentStreamVersion + 1 + index;
            return ToDocument(streamId, streamVersion, @event, committedAt);
        });

        try
        {
            var insertManyOptions = new InsertManyOptions { IsOrdered = true };
            await collection.InsertManyAsync(documents, insertManyOptions, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new WrongExpectedVersionException(streamId, expectedVersion.Value, currentStreamVersion, ex);
        }
    }

    public async Task<ReadStreamResult<StoredEventData<TPayload>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("header.streamId", streamId);

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
            return ReadStreamResult<StoredEventData<TPayload>>.NotFound();

        return ReadStreamResult<StoredEventData<TPayload>>.Success(Enumerate());

        async IAsyncEnumerable<StoredEventData<TPayload>> Enumerate()
        {
            using (cursor)
            {
                do
                {
                    foreach (var doc in cursor.Current)
                    {
                        var metadata = doc
                            .GetValueByPath("header.metadata")
                            .AsBsonDocument
                            .ToDictionary(x => x.Name, x => x.Value.AsString);

                        var @event = new StoredEventData<TPayload>(
                            doc.GetValueByPath("header.streamId").AsString,
                            doc.GetValueByPath("header.streamVersion").AsInt64,
                            doc.GetValueByPath("header.eventName").AsString,
                            toPayload(doc.GetValueByPath("payload")),
                            metadata,
                            doc.GetValueByPath("header.committedAt").AsUniversalTime);

                        yield return @event;
                    }
                } while (await cursor.MoveNextAsync(cancellationToken));
            }
        }
    }

    private BsonDocument ToDocument(
        string streamId,
        long streamVersion,
        EventData<TPayload> @event,
        DateTime committedAt)
    {
        var doc = new BsonDocument();
        doc.SetValueByPath("header.streamId", streamId);
        doc.SetValueByPath("header.streamVersion", streamVersion);
        doc.SetValueByPath("header.eventName", @event.EventName);
        doc.SetValueByPath("payload", toBsonValue(@event.Payload));
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