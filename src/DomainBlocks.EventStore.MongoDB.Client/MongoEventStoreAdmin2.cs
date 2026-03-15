using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public static class MongoEventStoreAdmin2
{
    public static async Task EnsureInitializedAsync(
        IMongoClient mongoClient,
        EventStoreNamespaceSettings namespaceSettings,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(namespaceSettings.DatabaseName);
        var appendRequests = db.GetCollection<AppendRequest>(namespaceSettings.AppendRequestsCollectionName);
        var loggedEvents = db.GetCollection<LoggedEvent>(namespaceSettings.LoggedEventsCollectionName);

        await EnsureAppendRequestsIndexesAsync(appendRequests, cancellationToken);
        await EnsureLoggedEventsIndexesAsync(loggedEvents, cancellationToken);

        await db.CreateCollectionAsync(
            namespaceSettings.LeasesCollectionName,
            new CreateCollectionOptions
            {
                ChangeStreamPreAndPostImagesOptions = new ChangeStreamPreAndPostImagesOptions
                {
                    Enabled = true
                }
            },
            cancellationToken);

        // var command = new BsonDocument
        // {
        //     { "collMod", namespaceSettings.LeasesCollectionName },
        //     { "changeStreamPreAndPostImages", new BsonDocument("enabled", true) }
        // };
        //
        // await db.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
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
            new(streamKey, new CreateIndexOptions { Unique = false }),
            new(commitKey, new CreateIndexOptions { Unique = false })
        ];

        return appendRequests.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}