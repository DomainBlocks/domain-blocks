using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreBenchmarkTests : EventStoreBenchmarkTests<StreamPosition, LogPosition>
{
    private MongoEventStoreOptions _options = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _options = new MongoEventStoreOptions { DatabaseName = "dbx_es_benchmark_tests" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.MongoClient, _options);
    }

    protected override IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        var eventCodec = TestMongoEventCodec.Create(eventTypeMap, eventFormat, contractMappers);

        return MongoEventStore.Create(
            SetUpFixture.MongoClient,
            eventCodec,
            _options,
            SetUpFixture.LoggerFactory.CreateLogger($"MongoEventStore{loggerNameSuffix}"));
    }
}