using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Testing.Integration.MongoDB;

public static class MongoEventStoreAdapterFactory
{
    public static async Task<IEventStoreClientAdapter<BsonValue, BsonValue>> CreateAsync(
        CancellationToken cancellationToken)
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault();

        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options, cancellationToken);

        return MongoEventStoreClientAdapter.Create(database, options);
    }
}