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
/// The store with a real synchronous standby, which the primary can cut off from its WAL.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PostgresSynchronousStandbyTests
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(1);
    private static readonly EventTypeMap EventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    private readonly PostgresEventStoreOptions _options = new() { Schema = "dbx_standby" };

    private ReplicatedPostgresServer _server = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await ReplicatedPostgresServer.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SessionWithSynchronousCommitOff_WaitsForStandby(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource(x => x.Options = "-c synchronous_commit=off");
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.CutOffStandbyAsync();

        var append = eventStore.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "e" })],
            cancellationToken: ct);

        var pid = await WaitForSyncRepAsync(ct);

        // A commit whose wait ended soon after it began would have completed during the hold.
        await Task.Delay(HoldDuration, ct);
        (await IsWaitingForSyncRepAsync(pid, ct)).ShouldBeTrue();
        append.IsCompleted.ShouldBeFalse();

        await _server.ReconnectStandbyAsync();
        await append;
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventDeliveredButNotVisible_ReplaysEventOnceVisible(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await using var subscription1 = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        (await subscription1.MoveNextAsync()).ShouldBeTrue();
        subscription1.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

        // With WAL replay paused, the standby flushes the event but doesn't apply it, and a commit with remote_apply
        // waits until the standby has applied it. So the event stays invisible on the primary while the feed has it.
        await _server.PauseWalReplayOnStandbyAsync();
        var append = AppendWithRemoteApplyAsync(dataSource, "e", ct);

        (await subscription1.MoveNextAsync()).ShouldBeTrue();
        subscription1.Current.Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });

        // The second subscription attaches after the feed delivered the event, so only its catch-up can deliver it.
        await using var subscription2 = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        var next = subscription2.MoveNextAsync().AsTask();

        // The event is still invisible after the hold, so it was invisible throughout, and the catch-up waits too.
        await Task.Delay(HoldDuration, ct);
        (await CountEventsAsync(dataSource, ct)).ShouldBe(0);
        next.IsCompleted.ShouldBeFalse();

        await _server.ResumeWalReplayOnStandbyAsync();
        await append;

        (await next).ShouldBeTrue();
        subscription2.Current.Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventSkippedBySlotButNotVisible_ReplaysEventOnceVisible(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        // The subscription's slot can't become consistent while X runs.
        await using var x = await XidTransaction.BeginAsync(dataSource, ct);
        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        var next = subscription.MoveNextAsync().AsTask();
        await WaitForXidLockWaitAsync(dataSource, x.Xid, ct);

        // Once X ends, the slot waits for Y, which started after X was recorded as running.
        await using var y = await XidTransaction.BeginAsync(dataSource, ct);
        await x.CommitAsync(ct);
        await WaitForXidLockWaitAsync(dataSource, y.Xid, ct);

        // The event commits and waits for the standby to apply it, so queries can't see it. Once Y ends, the slot
        // becomes consistent at a point after the event's commit, so it won't stream the event either.
        await _server.PauseReplayAsync();
        var append = AppendWithRemoteApplyAsync(dataSource, "e", ct);
        await WaitForSyncRepAsync(ct);
        await y.CommitAsync(ct);

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await _server.ResumeReplayAsync();
        await append;

        (await next).ShouldBeTrue();
        subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    // Calls append_events directly, and commits waiting for the standby to apply the event rather than flush it.
    private async Task AppendWithRemoteApplyAsync(NpgsqlDataSource dataSource, string value, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var client = new AppendFunctionClient(dataSource, _options.Schema);
        var e = JsonEvent(nameof(TestEvent), $"{{\"Value\": \"{value}\"}}");
        await client.AppendAsync(connection, [Any("s1", e)], ct);

        await using (var command = new NpgsqlCommand("SET LOCAL synchronous_commit = remote_apply", connection))
            await command.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
    }

    // Counts the events that queries on the primary see.
    private async Task<long> CountEventsAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT count(*) FROM {new SchemaObjectNames(_options.Schema).EventLog}");

        return (long)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private static async Task WaitForXidLockWaitAsync(NpgsqlDataSource dataSource, string xid, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (SELECT 1
                           FROM pg_locks
                           WHERE locktype = 'transactionid' AND NOT granted AND transactionid::text = $1)
            """);

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = xid });

        while (!(bool)(await command.ExecuteScalarAsync(ct))!)
            await Task.Delay(20, ct);
    }

    // Returns the process ID of a backend whose commit waits for the standby, once there is one.
    private async Task<int> WaitForSyncRepAsync(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();

        await using var command = dataSource.CreateCommand(
            "SELECT pid FROM pg_stat_activity WHERE wait_event = 'SyncRep' LIMIT 1");

        while (true)
        {
            if (await command.ExecuteScalarAsync(ct) is int pid)
                return pid;

            await Task.Delay(20, ct);
        }
    }

    private async Task<bool> IsWaitingForSyncRepAsync(int pid, CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();

        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1 AND wait_event = 'SyncRep')");

        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pid });
        return (bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private EventStore CreateEventStore(NpgsqlDataSource dataSource)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(_options)
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseLogger(PostgresTestEnvironment.LoggerFactory.CreateLogger(nameof(PostgresSynchronousStandbyTests)))
            .Build();
    }

    private NpgsqlDataSource CreateDataSource(Action<NpgsqlConnectionStringBuilder>? configure = null)
    {
        // The feed's replication connection uses this connection string, so it keeps the password.
        var connectionString = new NpgsqlConnectionStringBuilder(_server.PrimaryConnectionString)
        {
            PersistSecurityInfo = true
        };

        configure?.Invoke(connectionString);

        var builder = new NpgsqlDataSourceBuilder(connectionString.ConnectionString);
        builder.UsePostgresEventStore(_options);
        return builder.Build();
    }

    // A transaction that has taken an xid, so a slot being created waits for it to end.
    private sealed class XidTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction, string xid) :
        IAsyncDisposable
    {
        public string Xid { get; } = xid;

        public static async Task<XidTransaction> BeginAsync(NpgsqlDataSource dataSource, CancellationToken ct)
        {
            var connection = await dataSource.OpenConnectionAsync(ct);
            var transaction = await connection.BeginTransactionAsync(ct);

            await using var command = new NpgsqlCommand("SELECT pg_current_xact_id()::text", connection);
            var xid = (string)(await command.ExecuteScalarAsync(ct))!;

            return new XidTransaction(connection, transaction, xid);
        }

        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}