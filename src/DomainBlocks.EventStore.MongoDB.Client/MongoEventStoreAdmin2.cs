using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public static class MongoEventStoreAdmin2
{
    public static async Task EnsureIndexesAsync(
        IMongoClient mongoClient,
        EventStoreCollectionOptions2 collectionOptions,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var appendRequests = db.GetCollection<AppendRequest>(collectionOptions.AppendRequestsCollectionName);
        var loggedEvents = db.GetCollection<LoggedEvent>(collectionOptions.LoggedEventsCollectionName);

        await EnsureAppendRequestsIndexesAsync(appendRequests, cancellationToken);
        await EnsureLoggedEventsIndexesAsync(loggedEvents, cancellationToken);
    }

    private static Task EnsureAppendRequestsIndexesAsync(
        IMongoCollection<AppendRequest> appendRequests,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<AppendRequest>.IndexKeys;

        var createdAtUtc = indexBuilder.Ascending(x => x.CreatedAtUtc);

        CreateIndexModel<AppendRequest>[] indexModels =
        [
            new(createdAtUtc)
        ];

        return appendRequests.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }

    private static Task EnsureLoggedEventsIndexesAsync(
        IMongoCollection<LoggedEvent> appendRequests,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<LoggedEvent>.IndexKeys;
        var streamKey = indexBuilder.Ascending(x => x.StreamId).Ascending(x => x.StreamVersion);
        var commitKey = indexBuilder.Ascending(x => x.CommitId).Ascending(x => x.CommitIndex);

        CreateIndexModel<LoggedEvent>[] indexModels =
        [
            new(streamKey, new CreateIndexOptions { Unique = true }),
            new(commitKey, new CreateIndexOptions { Unique = true })
        ];

        return appendRequests.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}