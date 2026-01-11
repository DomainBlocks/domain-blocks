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
        var eventsCollection = db.GetCollection<EventDocument>(collectionOptions.EventsCollectionName);
        var streamCommitsCollection = db.GetCollection<StreamCommit>(collectionOptions.StreamCommitsCollectionName);

        await EnsureEventsCollectionIndexesAsync(eventsCollection, cancellationToken);
        await EnsureStreamCommitsCollectionIndexesAsync(streamCommitsCollection, cancellationToken);
    }

    private static Task EnsureEventsCollectionIndexesAsync(
        IMongoCollection<EventDocument> eventsCollection,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<EventDocument>.IndexKeys;

        var uniqueKey = indexBuilder
            .Ascending(x => x.StreamId)
            .Ascending(x => x.StreamVersion);

        var commitIdAndStreamVersion = indexBuilder
            .Ascending(x => x.CommitId)
            .Ascending(x => x.StreamVersion);

        var createdAtUtc = indexBuilder.Descending(x => x.CreatedAtUtc);

        CreateIndexModel<EventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(commitIdAndStreamVersion),
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
            .Descending(x => x.StartStreamVersion);

        var committedAtUtc = indexBuilder.Descending(x => x.CommittedAtUtc);
        var commitId = indexBuilder.Ascending(x => x.CommitId);
        var startGlobalPosition = indexBuilder.Ascending(x => x.StartGlobalPosition);

        CreateIndexModel<StreamCommit>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(committedAtUtc),
            new(commitId, new CreateIndexOptions { Unique = true }),
            new(startGlobalPosition, new CreateIndexOptions { Unique = true })
        ];

        return streamCommitsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}