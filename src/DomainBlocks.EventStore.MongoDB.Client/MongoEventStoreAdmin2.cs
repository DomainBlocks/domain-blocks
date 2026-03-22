using DomainBlocks.EventStore.MongoDB.Client.Schema2;
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
        var eventLog = db.GetCollection<EventLogEntry>(namespaceSettings.EventLogCollectionName);

        await EnsureAppendRequestsIndexesAsync(appendRequests, cancellationToken);
        await EnsureEventLogIndexesAsync(eventLog, cancellationToken);
    }

    private static Task EnsureAppendRequestsIndexesAsync(
        IMongoCollection<AppendRequest> appendRequests,
        CancellationToken cancellationToken = default)
    {
        var builder = Builders<AppendRequest>.IndexKeys;

        CreateIndexModel<AppendRequest>[] indexModels =
        [
            new(builder.Ascending(x => x.CreatedAtUtc))
        ];

        return appendRequests.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }

    private static Task EnsureEventLogIndexesAsync(
        IMongoCollection<EventLogEntry> appendRequests,
        CancellationToken cancellationToken = default)
    {
        var builder = Builders<EventLogEntry>.IndexKeys;

        CreateIndexModel<EventLogEntry>[] indexModels =
        [
            new(builder.Ascending(x => x.Epoch)),
            new(builder.Ascending(x => x.StreamId).Ascending(x => x.StreamVersion)),
            new(builder.Ascending(x => x.CommitId))
        ];

        return appendRequests.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}