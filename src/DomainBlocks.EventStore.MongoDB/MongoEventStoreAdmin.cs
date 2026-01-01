using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    public static Task EnsureIndexesAsync<TEventDocument>(
        string connectionString,
        CollectionNamespace collectionNamespace,
        EventDocumentMap<TEventDocument> documentMap,
        CancellationToken cancellationToken = default)
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        return EnsureIndexesAsync(settings, collectionNamespace, documentMap, cancellationToken);
    }

    public static async Task EnsureIndexesAsync<TEventDocument>(
        MongoClientSettings settings,
        CollectionNamespace collectionNamespace,
        EventDocumentMap<TEventDocument> documentMap,
        CancellationToken cancellationToken = default)
    {
        using var client = new MongoClient(settings);
        await EnsureIndexesAsync(client, collectionNamespace, documentMap, cancellationToken);
    }

    public static Task EnsureIndexesAsync<TEventDocument>(
        MongoClient client,
        CollectionNamespace collectionNamespace,
        EventDocumentMap<TEventDocument> documentMap,
        CancellationToken cancellationToken = default)
    {
        var database = client.GetDatabase(collectionNamespace.DatabaseNamespace.DatabaseName);
        var collection = database.GetCollection<TEventDocument>(collectionNamespace.CollectionName);

        var indexBuilder = Builders<TEventDocument>.IndexKeys;
        var uniqueKey = indexBuilder.Ascending(documentMap.StreamIdField).Ascending(documentMap.StreamVersionField);
        var createdAt = indexBuilder.Ascending(documentMap.CreatedAtField);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(createdAt)
        ];

        return collection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}