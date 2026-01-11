using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Generic;

public static class MongoEventStoreAdmin
{
    public static async Task EnsureIndexesAsync<TEventDocument>(
        IMongoClient mongoClient,
        EventStoreCollectionOptions collectionOptions,
        EventDocumentSchema<TEventDocument> eventDocumentSchema,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var eventsCollection = db.GetCollection<TEventDocument>(collectionOptions.EventsCollectionName);

        await EnsureEventsCollectionIndexesAsync(eventsCollection, eventDocumentSchema, cancellationToken);
    }

    private static Task EnsureEventsCollectionIndexesAsync<TEventDocument>(
        IMongoCollection<TEventDocument> eventsCollection,
        EventDocumentSchema<TEventDocument> eventDocumentSchema,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<TEventDocument>.IndexKeys;

        var uniqueKey = indexBuilder
            .Ascending(eventDocumentSchema.StreamIdField)
            .Ascending(eventDocumentSchema.StreamVersionField);

        var createdAtUtc = indexBuilder.Descending(eventDocumentSchema.CreatedAtUtcField);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(createdAtUtc)
        ];

        return eventsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}