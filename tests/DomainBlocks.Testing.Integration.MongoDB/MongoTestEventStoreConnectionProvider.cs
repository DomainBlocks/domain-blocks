using DomainBlocks.EventStore.MongoDB;
using MongoDB.Bson;

namespace DomainBlocks.Testing.Integration.MongoDB;

public static class MongoTestEventStoreConnectionProvider
{
    public static async Task<IMongoEventStoreConnectionProvider<BsonValue, BsonValue>> CreateAsync()
    {
        var connectionOptions = MongoEventStoreConnectionOptions.CreateDefault();

        var provider = MongoEventStoreConnectionProvider.FromConnectionString(
            MongoConnectionStrings.Default,
            connectionOptions);

        await MongoEventStoreAdmin.EnsureIndexesAsync(
            MongoConnectionStrings.Default,
            connectionOptions.CollectionNamespace,
            connectionOptions.DocumentMap);

        return provider;
    }
}