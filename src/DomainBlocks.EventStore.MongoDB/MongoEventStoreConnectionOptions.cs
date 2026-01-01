using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreConnectionOptions
{
    private const string DefaultDatabaseName = "test";
    private const string DefaultCollectionName = "domainblocks.events";

    public static MongoEventStoreConnectionOptions<EventDocument, BsonValue, BsonValue> CreateDefault(
        string databaseName = DefaultDatabaseName,
        string collectionName = DefaultCollectionName)
    {
        return new MongoEventStoreConnectionOptions<EventDocument, BsonValue, BsonValue>
        {
            CollectionNamespace = new CollectionNamespace(databaseName, collectionName),
            DocumentConverter = new EventDocumentConverter(),
            DocumentMap = new EventDocumentMap<EventDocument>
            {
                StreamId = doc => doc.StreamId,
                StreamVersion = doc => doc.StreamVersion,
                CreatedAt = doc => doc.CreatedAt
            }
        };
    }
}

public sealed class MongoEventStoreConnectionOptions<TEventDocument, TEventData, TMetadata>
    where TEventData : notnull
    where TMetadata : notnull
{
    public required CollectionNamespace CollectionNamespace { get; init; }
    public required IEventDocumentConverter<TEventDocument, TEventData, TMetadata> DocumentConverter { get; init; }
    public required EventDocumentMap<TEventDocument> DocumentMap { get; init; }
}