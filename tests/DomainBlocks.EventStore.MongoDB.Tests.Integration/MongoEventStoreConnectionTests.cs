using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreConnectionTests : EventStoreConnectionTests<BsonValue, BsonValue>
{
    protected override async Task<IEventStoreConnectionProvider<BsonValue, BsonValue>> GetConnectionProviderAsync()
    {
        var provider = await MongoTestEventStoreConnectionProvider.CreateAsync();
        return provider;
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