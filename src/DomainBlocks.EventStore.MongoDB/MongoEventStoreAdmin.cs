using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    public static async Task EnsureInitializedAsync(
        IMongoClient mongoClient,
        MongoEventStoreOptions options,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(options.DatabaseName);
        var eventLog = db.GetCollection<EventLogEntry>(options.EventLogCollectionName);
        var builder = Builders<EventLogEntry>.IndexKeys;

        CreateIndexModel<EventLogEntry>[] indexModels =
        [
            new(builder.Ascending(x => x.StreamId).Ascending(x => x.StreamVersion),
                new CreateIndexOptions
                {
                    Name = EventLogIndexNames.UniqueStreamVersion,
                    Unique = true
                }),

            new(builder.Ascending(x => x.CommitId), new CreateIndexOptions { Name = EventLogIndexNames.CommitId })
        ];

        await eventLog.Indexes.CreateManyAsync(indexModels, cancellationToken).ConfigureAwait(false);
    }
}