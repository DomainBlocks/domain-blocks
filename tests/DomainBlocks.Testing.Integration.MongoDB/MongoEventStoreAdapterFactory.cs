using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using MongoDB.Driver;

namespace DomainBlocks.Testing.Integration.MongoDB;

public static class MongoEventStoreAdapterFactory
{
    public static async Task<IEventStoreClientAdapter<TSerialized>> CreateAsync<TSerialized>(
        CancellationToken cancellationToken)
        where TSerialized : notnull
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault<TSerialized>();

        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options, cancellationToken);

        return MongoEventStoreClientAdapter.Create(database, options);
    }
}