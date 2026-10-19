using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Integration.AppendFunctionClient;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

using EventStore = IEventStore<object, string, StreamPosition, LogPosition>;

/// <summary>
/// The replication barrier with a real synchronous standby, which the primary can cut off from its WAL. Unlike the
/// barrier tests, which clear <c>synchronous_standby_names</c> to release a waiting commit, these show that the standby
/// has every event that a subscription was given, by promoting it. An append whose wait for the standby is canceled
/// makes its event visible on the primary only.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PostgresSynchronousStandbyTests
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(1);
    private static readonly EventTypeMap EventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    private readonly PostgresEventStoreOptions _options = new()
    {
        Schema = "dbx_standby",
        Tail = new EventLogTailOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(200),
            RetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(200)
        }
    };

    private ReplicatedPostgresServer _server = null!;
    private NpgsqlDataSource _dataSource = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await ReplicatedPostgresServer.StartAsync();
        _dataSource = CreateDataSource(_server.PrimaryConnectionString);

        await PostgresEventStoreAdmin.EnsureInitializedAsync(_dataSource, _options);
        await ExecuteAsync($"ALTER SYSTEM SET synchronous_standby_names = '{ReplicatedPostgresServer.StandbyName}'");
        await ExecuteAsync("SELECT pg_reload_conf()");

        // Each session takes up the setting when it next reloads its configuration.
        while (true)
        {
            await using var command = _dataSource.CreateCommand("SHOW synchronous_standby_names");

            if ((string?)await command.ExecuteScalarAsync() == ReplicatedPostgresServer.StandbyName)
                return;

            await Task.Delay(20);
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventNotOnStandby_IsDeliveredOnlyOnceStandbyHasIt(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        (await subscription.MoveNextAsync()).ShouldBeTrue();
        subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

        await _server.CutOffStandbyAsync();

        await AppendWithCanceledWaitAsync("live", ct);
        var next = subscription.MoveNextAsync().AsTask();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await _server.ReconnectStandbyAsync();

        (await next).ShouldBeTrue();
        subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "live" });

        // The primary fails, and a failover promotes the standby, which has the event.
        await _server.CrashPrimaryAsync();
        await _server.PromoteStandbyAsync();

        await using var standby = NpgsqlDataSource.Create(_server.StandbyConnectionString);
        await using var count = standby.CreateCommand($"SELECT count(*) FROM {_options.Schema}.event_log");
        ((long)(await count.ExecuteScalarAsync(ct))!).ShouldBe(1);
    }

    // Calls append_events directly, so that the appender's handling of the canceled wait plays no part.
    private async Task AppendWithCanceledWaitAsync(string value, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);

        await using (var command = new NpgsqlCommand("SET synchronous_commit = on", connection))
            await command.ExecuteNonQueryAsync(ct);

        var client = new AppendFunctionClient(_dataSource, _options.Schema);
        var e = JsonEvent(nameof(TestEvent), $"{{\"Value\": \"{value}\"}}");
        var append = client.AppendAsync(connection, [Any("s1", e)], ct);

        await using (var waiting = _dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1 AND wait_event = 'SyncRep')"))
        {
            waiting.Parameters.Add(new NpgsqlParameter<int> { TypedValue = connection.ProcessID });

            while (!(bool)(await waiting.ExecuteScalarAsync(ct))!)
                await Task.Delay(20, ct);
        }

        await ExecuteAsync($"SELECT pg_cancel_backend({connection.ProcessID})");
        await append;
    }

    private EventStore CreateEventStore()
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(_dataSource)
            .UseOptions(_options)
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseLogger(PostgresTestEnvironment.LoggerFactory.CreateLogger(nameof(PostgresSynchronousStandbyTests)))
            .Build();
    }

    private NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UsePostgresEventStore(_options);
        return builder.Build();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }
}