using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    public static Task EnsureIndexesAsync<TEventDocument>(
        IMongoDatabase database,
        MongoEventStoreOptions<TEventDocument> options,
        CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<TEventDocument>(options.EventCollectionName);
        var indexBuilder = Builders<TEventDocument>.IndexKeys;
        var uniqueKey = indexBuilder.Ascending(options.StreamIdField).Ascending(options.StreamVersionField);
        var committedAt = indexBuilder.Ascending(options.CommittedAtField);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(committedAt)
        ];

        return collection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}