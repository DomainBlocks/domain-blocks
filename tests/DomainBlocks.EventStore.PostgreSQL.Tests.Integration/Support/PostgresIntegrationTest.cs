using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Support;

/// <summary>
/// Base of the PostgreSQL-specific tests: one schema per fixture, named after the fixture and emptied before each
/// test, with direct access to the schema through <see cref="Client"/>.
/// </summary>
public abstract class PostgresIntegrationTest(Action<PostgresEventStoreOptions>? configure = null)
{
    protected static readonly EventTypeMap DefaultEventTypeMap =
        EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    protected PostgresEventStoreHarness Harness { get; } = new(configure);

    protected PostgresEventStoreOptions Options => Harness.Options;

    protected string Schema => Options.Schema;

    protected AppendFunctionClient Client { get; private set; } = null!;

    protected static NpgsqlDataSource DataSource => PostgresTestEnvironment.DataSource;

    protected static ILoggerFactory LoggerFactory => PostgresTestEnvironment.LoggerFactory;

    [OneTimeSetUp]
    public async Task InitializeSchemaAsync()
    {
        await Harness.InitializeAsync(TestStoreName.For(this));
        Client = new AppendFunctionClient(DataSource, Schema);
    }

    [OneTimeTearDown]
    public Task DropSchemaAsync() => Harness.DropAsync();

    [SetUp]
    public Task ResetLogAsync() => Harness.ResetAsync();

    /// <summary>
    /// Creates a store over the fixture's schema with the default JSON codec and, optionally, different options.
    /// </summary>
    protected PostgresEventStore<object> CreateEventStore(
        string loggerNameSuffix = "",
        PostgresEventStoreOptions? options = null)
    {
        return Harness.CreateEventStore(
            TestPostgresEventCodec.Create<object>(DefaultEventTypeMap),
            options,
            loggerNameSuffix);
    }

    protected static AppendableEvent<object> Appendable(string value) => Appendable(new TestEvent { Value = value });

    protected static AppendableEvent<object> Appendable(TestEvent e) => AppendableEvent.Create<object>(e);
}