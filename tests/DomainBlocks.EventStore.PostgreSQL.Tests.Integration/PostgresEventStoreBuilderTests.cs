using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

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
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
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
    public async Task BorrowedPath_DisposingTheStore_LeavesTheDataSourceUsable()
    {
        await using var dataSource =
            PostgresTestEnvironment.CreateDataSource(b => b.UsePostgresEventStore(_options));

        var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        await store.EnsureInitializedAsync();
        await store.AppendAsync("s1", [AppendableEvent.Create<object>(new TestEvent { Value = "v" })]);
        await store.DisposeAsync();

        await using var connection = await dataSource.OpenConnectionAsync();
        connection.State.ShouldBe(System.Data.ConnectionState.Open);
    }

    [Test]
    public async Task Defaults_WithoutAnySerializerConfigured_RoundTripsWorks()
    {
        await using var dataSource =
            PostgresTestEnvironment.CreateDataSource(b => b.UsePostgresEventStore(_options));

        await using var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        await store.EnsureInitializedAsync();

        var appended = new TestEvent { Value = "json-by-default" };
        await store.AppendAsync("s1", [AppendableEvent.Create<object>(appended, [new("k", "v")])]);

        var read = (await store.ReadStream("s1").ToArrayAsync()).ShouldHaveSingleItem();
        read.Payload.ShouldBe(appended);
        read.Context.Metadata.ShouldBe(new Dictionary<string, string> { ["k"] = "v" });
    }

    private static string ConnectionStringWithoutPersistedSecurityInfo()
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresTestEnvironment.ConnectionString)
        {
            PersistSecurityInfo = false
        };

        return builder.ConnectionString;
    }
}