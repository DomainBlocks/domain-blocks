using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public static class MongoEventStoreAdmin
{
    public static async Task EnsureIndexesAsync(
        IMongoClient mongoClient,
        EventStoreCollectionOptions collectionOptions,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var streamCommitsCollection = db.GetCollection<StreamCommit>(collectionOptions.StreamCommitsCollectionName);

        await EnsureStreamCommitsCollectionIndexesAsync(streamCommitsCollection, cancellationToken);
    }

    private static Task EnsureStreamCommitsCollectionIndexesAsync(
        IMongoCollection<StreamCommit> streamCommitsCollection,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<StreamCommit>.IndexKeys;

        var uniqueKey = indexBuilder
            .Ascending(x => x.StreamId)
            .Descending(x => x.StartStreamVersion);

        var startGlobalPosition = indexBuilder.Ascending(x => x.StartGlobalPosition);
        var committedAtUtc = indexBuilder.Descending(x => x.CommittedAtUtc);

        CreateIndexModel<StreamCommit>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(startGlobalPosition, new CreateIndexOptions { Unique = true }),
            new(committedAtUtc)
        ];

        return streamCommitsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}