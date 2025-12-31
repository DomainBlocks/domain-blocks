using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    public static Task EnsureIndexesAsync<TEventDocument, TEventData, TMetadata>(
        IMongoDatabase database,
        MongoEventStoreOptions<TEventDocument, TEventData, TMetadata> options,
        CancellationToken cancellationToken = default)
        where TEventData : notnull
        where TMetadata : notnull
    {
        var collection = database.GetCollection<TEventDocument>(options.EventCollectionName);
        var indexBuilder = Builders<TEventDocument>.IndexKeys;
        var uniqueKey = indexBuilder.Ascending(options.StreamIdField).Ascending(options.StreamVersionField);
        var createdAt = indexBuilder.Ascending(options.CreatedAtField);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(createdAt)
        ];

        return collection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}