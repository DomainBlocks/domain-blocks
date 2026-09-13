using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.Contract;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson.Serialization;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Contract;

[TestFixture]
public class MongoEventStoreEventRepresentationTests() :
    EventStoreEventRepresentationTests<StreamPosition, LogPosition>(new MongoEventStoreHarness())
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