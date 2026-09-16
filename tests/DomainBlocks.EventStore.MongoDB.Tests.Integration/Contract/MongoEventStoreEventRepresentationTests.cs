using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson.Serialization;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Contract;

[TestFixture]
public class MongoEventStoreEventRepresentationTests() :
    EventStoreEventRepresentationTests<StreamPosition, LogPosition>(new MongoEventStoreTestHarness())
{
    static MongoEventStoreEventRepresentationTests()
    {
        BsonClassMap.RegisterClassMap<LimitOrderEvent>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }
}