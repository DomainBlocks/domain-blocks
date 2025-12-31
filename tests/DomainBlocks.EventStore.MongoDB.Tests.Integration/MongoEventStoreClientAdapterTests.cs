using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Testing.Integration;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientAdapterTests : EventStoreClientAdapterTests<BsonValue>
{
    protected override async Task<IEventStoreClientAdapter<BsonValue>> CreateEventStoreAdapterAsync()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault<BsonValue>();
        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options);

        return MongoEventStoreClientAdapter.Create(database, options);
    }

    protected override SerializedAppendEvent<BsonValue> CreateTestEvent(string eventName)
    {
        BsonValue value = new BsonDocument
        {
            { "TestProperty", "TestValue" }
        };

        return SerializedAppendEvent.Create(eventName, value);
    }
}