using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public static class MongoEventStoreAdmin
{
    public static async Task EnsureIndexesAsync(
        IMongoClient mongoClient,
        EventStoreCollectionOptions collectionOptions,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var streamCommitsCollection = db.GetCollection<Schema.StreamCommit>(collectionOptions.StreamCommitsCollectionName);

        await EnsureStreamCommitsCollectionIndexesAsync(streamCommitsCollection, cancellationToken);
    }

    private static Task EnsureStreamCommitsCollectionIndexesAsync(
        IMongoCollection<Schema.StreamCommit> streamCommitsCollection,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<Schema.StreamCommit>.IndexKeys;

        var streamStart = indexBuilder
            .Ascending(x => x.StreamId)
            .Ascending(x => x.StartStreamVersion);

        var streamEnd = indexBuilder
            .Ascending(x => x.StreamId)
            .Ascending(x => x.EndStreamVersion);

        var globalStart = indexBuilder.Ascending(x => x.StartGlobalPosition);
        var globalEnd = indexBuilder.Ascending(x => x.EndGlobalPosition);
        var committedAtUtc = indexBuilder.Descending(x => x.CommittedAtUtc);

        CreateIndexModel<Schema.StreamCommit>[] indexModels =
        [
            new(streamStart, new CreateIndexOptions { Unique = true }),
            new(streamEnd),
            new(globalStart),
            new(globalEnd),
            new(committedAtUtc)
        ];

        return streamCommitsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}