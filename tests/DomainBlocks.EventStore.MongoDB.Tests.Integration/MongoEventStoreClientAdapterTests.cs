using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Testing.Integration;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientAdapterTests : EventStoreClientAdapterTests<BsonDocument>
{
    protected override async Task<IEventStoreClientAdapter<BsonDocument>> CreateEventStoreAdapterAsync()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault<BsonDocument>();
        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options);

        return MongoEventStoreClientAdapter.Create(database, options);
    }

    protected override UncommittedEvent<BsonDocument> CreateTestEvent(string eventName)
    {
        var payload = new BsonDocument
        {
            { "TestProperty", "TestValue" }
        };

        return UncommittedEvent.Create(new UncommittedEventHeader(eventName), payload);
    }
}