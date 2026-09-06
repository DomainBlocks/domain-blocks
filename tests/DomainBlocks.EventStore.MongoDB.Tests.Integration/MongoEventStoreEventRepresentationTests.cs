using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson.Serialization;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreEventRepresentationTests : EventStoreEventRepresentationTests<StreamPosition, LogPosition>
{
    private MongoEventStoreOptions _options = null!;

    static MongoEventStoreEventRepresentationTests()
    {
        BsonClassMap.RegisterClassMap<LimitOrderEvent>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _options = new MongoEventStoreOptions { DatabaseName = "dbx_es_event_representation_tests" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.MongoClient, _options);
    }

    protected override IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        string name = "default",
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null)
    {
        var eventCodec = TestMongoEventCodec.Create(eventTypeMap, eventFormat, contractMappers);

        return MongoEventStore.Create(
            SetUpFixture.MongoClient,
            eventCodec,
            _options,
            SetUpFixture.LoggerFactory.CreateLogger($"MongoEventStore_{name}"));
    }
}