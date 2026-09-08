using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreSubscriptionTests : EventStoreSubscriptionTests<StreamPosition, LogPosition>
{
    private const string Schema = "dbx_es_subscription_tests";
    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema };

    private AppendFunctionClient _client = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, Options);
        _client = new AppendFunctionClient(SetUpFixture.DataSource, Schema);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, Options);
    }

    [SetUp]
    public new async Task SetUp()
    {
        // Each subscription test assumes an empty event log.
        await _client.ResetAsync();
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
            Options,
            SetUpFixture.LoggerFactory.CreateLogger($"PostgresEventStore{loggerNameSuffix}"));
    }
}
