using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    public static Task EnsureIndexesAsync<TEventDocument>(
        string connectionString,
        EventCollectionOptions<TEventDocument> collectionOptions,
        CancellationToken cancellationToken = default)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        return EnsureIndexesAsync(settings, collectionOptions, cancellationToken);
    }

    public static async Task EnsureIndexesAsync<TEventDocument>(
        MongoClientSettings clientSettings,
        EventCollectionOptions<TEventDocument> collectionOptions,
        CancellationToken cancellationToken = default)
    {
        using var client = new MongoClient(clientSettings);
        await EnsureIndexesAsync(client, collectionOptions, cancellationToken);
    }

    public static Task EnsureIndexesAsync<TEventDocument>(
        MongoClient client,
        EventCollectionOptions<TEventDocument> collectionOptions,
        CancellationToken cancellationToken = default)
    {
        var collection = client.GetCollection<TEventDocument>(collectionOptions.CollectionNamespace);
        return EnsureIndexesAsync(collection, collectionOptions, cancellationToken);
    }

    public static Task EnsureIndexesAsync<TEventDocument>(
        IMongoCollection<TEventDocument> collection,
        EventCollectionOptions<TEventDocument> collectionOptions,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<TEventDocument>.IndexKeys;

        var uniqueKey = indexBuilder
            .Ascending(collectionOptions.DocumentSchema.StreamIdField)
            .Ascending(collectionOptions.DocumentSchema.StreamVersionField);

        var createdAtUtc = indexBuilder.Ascending(collectionOptions.DocumentSchema.CreatedAtUtcField);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(createdAtUtc)
        ];

        return collection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}