using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// The builder's two connection paths against a real server. The owned path uses a connection string without
/// <c>Persist Security Info</c>, so a working live subscription proves the replication connection was defaulted from
/// the raw connection string rather than from the data source.
/// </summary>
public class PostgresEventStoreBuilderTests
{
    private readonly PostgresEventStoreOptions _options = new() { Schema = $"bld_{Guid.NewGuid():N}" };

    [TearDown]
    public Task DropSchemaAsync() => PostgresEventStoreAdmin.DropAsync(PostgresTestEnvironment.DataSource, _options);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task OwnedPath_InitializeAppendReadSubscribeDispose_Works(CancellationToken ct)
    {
        var store = new PostgresEventStoreBuilder<object>()
            .UseConnectionString(ConnectionStringWithoutPersistedSecurityInfo())
            .UseOptions(_options)
            .MapEvent<TestEvent>()
            .UseLoggerFactory(PostgresTestEnvironment.LoggerFactory)
            .Build();

        await using (store)
        {
            await store.EnsureInitializedAsync(ct);

            var appended = new TestEvent { Value = "owned" };
            await store.AppendAsync("s1", [AppendableEvent.Create<object>(appended)], cancellationToken: ct);

            var read = await store.ReadStream("s1").ToArrayAsync(ct);
            read.ShouldHaveSingleItem().Payload.ShouldBe(appended);

            await using var subscription = store.SubscribeToAll().GetAsyncEnumerator(ct);
            (await subscription.MoveNextAsync()).ShouldBeTrue();
            subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

            var live = new TestEvent { Value = "live" };
            await store.AppendAsync("s2", [AppendableEvent.Create<object>(live)], cancellationToken: ct);

            (await subscription.MoveNextAsync()).ShouldBeTrue();
            subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(live);
        }
    }

    [Test]
    public void OwnedPath_BuildTwice_Throws()
    {
        var builder = new PostgresEventStoreBuilder<object>()
            .UseConnectionString(PostgresTestEnvironment.ConnectionString)
            .UseOptions(_options)
            .MapEvent<TestEvent>();

        var first = builder.Build();

        var ex = Should.Throw<InvalidOperationException>(builder.Build);
        ex.Message.ShouldContain("one store");

        first.DisposeAsync().AsTask().Wait();
    }

    [Test]
    public async Task BorrowedPath_DisposingTheStore_LeavesTheDataSourceUsable()
    {
        await using var dataSource =
            PostgresTestEnvironment.CreateDataSource(b => b.UsePostgresEventStore(_options));

        var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(_options)
            .MapEvent<TestEvent>()
            .Build();

        await store.EnsureInitializedAsync();
        await store.AppendAsync("s1", [AppendableEvent.Create<object>(new TestEvent { Value = "v" })]);
        await store.DisposeAsync();

        await using var connection = await dataSource.OpenConnectionAsync();
        connection.State.ShouldBe(System.Data.ConnectionState.Open);
    }

    [Test]
    public async Task Defaults_RoundTripWithoutAnySerializerConfigured()
    {
        await using var dataSource =
            PostgresTestEnvironment.CreateDataSource(b => b.UsePostgresEventStore(_options));

        await using var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(_options)
            .MapEvent<TestEvent>()
            .Build();

        await store.EnsureInitializedAsync();

        var appended = new TestEvent { Value = "json-by-default" };
        await store.AppendAsync("s1", [AppendableEvent.Create<object>(appended, [new("k", "v")])]);

        var read = (await store.ReadStream("s1").ToArrayAsync()).ShouldHaveSingleItem();
        read.Payload.ShouldBe(appended);
        read.Context.Metadata.ShouldBe(new Dictionary<string, string> { ["k"] = "v" });
    }

    private static string ConnectionStringWithoutPersistedSecurityInfo() =>
        new NpgsqlConnectionStringBuilder(PostgresTestEnvironment.ConnectionString) { PersistSecurityInfo = false }
            .ConnectionString;
}