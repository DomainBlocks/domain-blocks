using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    public static Task EnsureIndexesAsync<TEventDocument>(
        IMongoClient mongoClient,
        EventStoreCollectionOptions collectionOptions,
        EventDocumentSchema<TEventDocument> eventDocumentSchema,
        CancellationToken cancellationToken = default)
    {
        var database = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var eventsCollection = database.GetCollection<TEventDocument>(collectionOptions.EventsCollectionName);

        var streamCommitsCollection =
            database.GetCollection<StreamCommit>(collectionOptions.StreamCommitsCollectionName);

        return EnsureIndexesAsync(eventsCollection, eventDocumentSchema, streamCommitsCollection, cancellationToken);
    }

    public static async Task EnsureIndexesAsync<TEventDocument>(
        IMongoCollection<TEventDocument> eventsCollection,
        EventDocumentSchema<TEventDocument> eventDocumentSchema,
        IMongoCollection<StreamCommit> streamCommitsCollection,
        CancellationToken cancellationToken = default)
    {
        await EnsureEventsCollectionIndexesAsync(eventsCollection, eventDocumentSchema, cancellationToken);
        await EnsureStreamCommitsCollectionIndexesAsync(streamCommitsCollection, cancellationToken);
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

        var createdAtUtc = indexBuilder.Ascending(eventDocumentSchema.CreatedAtUtcField);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(createdAtUtc)
        ];

        return eventsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }

    private static Task EnsureStreamCommitsCollectionIndexesAsync(
        IMongoCollection<StreamCommit> streamCommitsCollection,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<StreamCommit>.IndexKeys;

        var uniqueKey = indexBuilder
            .Ascending(x => x.StreamId)
            .Descending(x => x.StartVersion);

        var createdAtUtc = indexBuilder.Ascending(x => x.CommittedAtUtc);

        CreateIndexModel<StreamCommit>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(createdAtUtc)
        ];

        return streamCommitsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}