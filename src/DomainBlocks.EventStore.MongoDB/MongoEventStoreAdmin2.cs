using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin2
{
    public static async Task EnsureInitializedAsync(
        IMongoClient mongoClient,
        MongoEventStoreClientOptions2 options,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(options.DatabaseName);
        var eventLog = db.GetCollection<EventLogEntry>(options.EventLogCollectionName);
        var builder = Builders<EventLogEntry>.IndexKeys;
        var uniqueOptions = new CreateIndexOptions { Unique = true };

        CreateIndexModel<EventLogEntry>[] indexModels =
        [
            new(builder.Ascending(x => x.StreamId).Ascending(x => x.StreamVersion), uniqueOptions),
            new(builder.Ascending(x => x.CommitId))
        ];

        await eventLog.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}