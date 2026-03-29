using DomainBlocks.EventStore.MongoDB.Client.Schema;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public static class MongoEventStoreAdmin
{
    public static async Task EnsureInitializedAsync(
        IMongoClient mongoClient,
        MongoEventStoreClientOptions options,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(options.DatabaseName);
        var appendRequests = db.GetCollection<AppendRequest>(options.AppendRequestsCollectionName);
        var eventLog = db.GetCollection<EventLogEntry>(options.EventLogCollectionName);

        await EnsureAppendRequestsIndexesAsync(appendRequests, options.AppendRequestTtl, cancellationToken);
        await EnsureEventLogIndexesAsync(eventLog, cancellationToken);
    }

    private static Task EnsureAppendRequestsIndexesAsync(
        IMongoCollection<AppendRequest> appendRequests,
        TimeSpan appendRequestTtl,
        CancellationToken cancellationToken = default)
    {
        var builder = Builders<AppendRequest>.IndexKeys;

        CreateIndexModel<AppendRequest>[] indexModels =
        [
            new(builder.Ascending(x => x.CreatedAtUtc), new CreateIndexOptions { ExpireAfter = appendRequestTtl })
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