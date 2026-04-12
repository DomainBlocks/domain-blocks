using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    public static async Task EnsureInitializedAsync(
        IMongoClient mongoClient,
        MongoEventStoreNodeOptions options,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(options.DatabaseName);
        var appendRequests = db.GetCollection<AppendRequest>(options.RequestsCollectionName);
        var eventLog = db.GetCollection<EventLogEntry>(options.EventLogCollectionName);

        await EnsureAppendRequestsIndexesAsync(appendRequests, options.RequestDocumentTtl, cancellationToken);
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
        IMongoCollection<EventLogEntry> eventLog,
        CancellationToken cancellationToken = default)
    {
        var builder = Builders<EventLogEntry>.IndexKeys;

        CreateIndexModel<EventLogEntry>[] indexModels =
        [
            new(builder.Ascending(x => x.StreamId).Ascending(x => x.Epoch).Ascending(x => x.StreamVersion)),
            new(builder.Ascending(x => x.CommitId).Ascending(x => x.Epoch))
        ];

        return eventLog.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}