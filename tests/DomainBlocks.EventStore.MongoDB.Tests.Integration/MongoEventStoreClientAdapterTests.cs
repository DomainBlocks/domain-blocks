using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Testing.Integration;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientAdapterTests : EventStoreClientAdapterTests<BsonValue, BsonValue>
{
    protected override async Task<IEventStoreClientAdapter<BsonValue, BsonValue>> CreateEventStoreAdapterAsync()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault();
        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options);

        return MongoEventStoreClientAdapter.Create(database, options);
    }

    protected override AppendEvent<BsonValue, BsonValue> CreateTestEvent(string eventName)
    {
        BsonValue value = new BsonDocument
        {
            { "TestProperty", "TestValue" }
        };

        return AppendEvent.Create<BsonValue, BsonValue>(eventName, value);
    }
}