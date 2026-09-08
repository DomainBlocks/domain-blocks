using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreEventRepresentationTests :
    EventStoreEventRepresentationTests<StreamPosition, LogPosition>
{
    private PostgresEventStoreOptions _options = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _options = new PostgresEventStoreOptions { Schema = "dbx_es_event_representation_tests" };
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, _options);
    }

    protected override IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        var eventCodec = TestPostgresEventCodec.Create(eventTypeMap, eventFormat, contractMappers);

        return PostgresEventStore.Create(
            SetUpFixture.DataSource,
            eventCodec,
            _options,
            SetUpFixture.LoggerFactory.CreateLogger($"PostgresEventStore{loggerNameSuffix}"));
    }
}
