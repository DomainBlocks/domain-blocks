using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreSubscriptionTests : EventStoreSubscriptionTests<StreamPosition, LogPosition>
{
    private MongoEventStoreOptions _options = null!;

    [SetUp]
    public new async Task SetUp()
    {
        _options = new MongoEventStoreOptions { DatabaseName = "dbx_es_subscription_tests" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.MongoClient, _options);
    }

    [TearDown]
    public new async Task TearDown()
    {
        // Each subscription test assumes an empty event log.
        await SetUpFixture.MongoClient.DropDatabaseAsync(_options.DatabaseName);
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