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

        await using var subscription1 = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        (await subscription1.NextAsync()).IsCaughtUp.ShouldBeTrue();

        // With WAL replay paused, the standby flushes the event but doesn't apply it, and a commit with remote_apply
        // waits until the standby has applied it. So the event stays invisible on the primary while the feed has it.
        await _server.PauseWalReplayOnStandbyAsync();
        await using var append = await RemoteApplyAppend.StartAsync(dataSource, _options.Schema, "e", ct);

        (await subscription1.NextEventAsync()).ShouldBe(new TestEvent { Value = "e" });

        // The second subscription attaches after the feed delivered the event, so only its catch-up can deliver it.
        await using var subscription2 = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        var next = subscription2.NextAsync();

        // The event is still invisible after the hold, so it was invisible throughout, and the catch-up waits too.
        await Task.Delay(HoldDuration, ct);
        (await CountEventsAsync(dataSource, ct)).ShouldBe(0);
        next.IsCompleted.ShouldBeFalse();

        await _server.ResumeWalReplayOnStandbyAsync();
        await append.Completion;

        (await next).Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventSkippedBySlotButNotVisible_ReplaysEventOnceVisible(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        // The subscription's slot can't become consistent while txn1 runs.
        await using var txn1 = await XidTransaction.BeginAsync(dataSource, ct);
        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        var next = subscription.NextAsync();
        await WaitForXidLockWaitAsync(dataSource, txn1.Xid, ct);

        // Once txn1 ends, the slot waits for txn2, which started after txn1 was recorded as running.
        await using var txn2 = await XidTransaction.BeginAsync(dataSource, ct);
        await txn1.CommitAsync(ct);
        await WaitForXidLockWaitAsync(dataSource, txn2.Xid, ct);

        // The event commits and waits for the standby to apply it, so queries can't see it. Once txn2 ends, the slot
        // becomes consistent at a point after the event's commit, so it won't stream the event either.
        await _server.PauseWalReplayOnStandbyAsync();
        await using var append = await RemoteApplyAppend.StartAsync(dataSource, _options.Schema, "e", ct);
        await WaitForSyncRepAsync(append.ProcessId, ct);
        await txn2.CommitAsync(ct);

        // Only the catch-up can deliver the event, so it must not report caught up while the event is invisible.
        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await _server.ResumeWalReplayOnStandbyAsync();
        await append.Completion;

        (await next).Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventVisibleButNotOnStandby_ReplaysEventOnceOnStandby(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.CutOffStandbyAsync();
        await AppendWithCanceledWaitAsync(dataSource, "e", ct);

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        var next = subscription.NextAsync();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await _server.ReconnectStandbyAsync();

        (await next).Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });

        // The primary fails, and a failover promotes the standby, which has the event.
        await _server.CrashPrimaryAsync();
        await _server.PromoteStandbyAsync();

        await using var standby = NpgsqlDataSource.Create(_server.StandbyConnectionString);
        await using var count = standby.CreateCommand($"SELECT count(*) FROM {_options.Schema}.event_log");
        ((long)(await count.ExecuteScalarAsync(ct)).ShouldNotBeNull()).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_EventVisibleButNotOnStandby_ReplaysEventOnceOnStandby(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.CutOffStandbyAsync();
        await AppendWithCanceledWaitAsync(dataSource, "e", ct);

        await using var subscription = eventStore.SubscribeToStream("s1", SubscriptionOrigin.Start).Probe(ct);
        var next = subscription.NextAsync();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await _server.ReconnectStandbyAsync();

        (await next).Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_BarrierWaitCanceled_Fails(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.CutOffStandbyAsync();
        await AppendWithCanceledWaitAsync(dataSource, "e", ct);

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        var next = subscription.NextAsync();

        // The barrier's commit completes as the append's did, without the standby.
        await CancelBackendAsync(dataSource, await WaitForSyncRepAsync(ct), ct);

        await Should.ThrowAsync<ReplicationBarrierException>(next);
    }

    // Calls append_events directly, and cancels its commit's wait for the standby, so the event is visible on the
    // primary although the standby doesn't have it.
    private async Task AppendWithCanceledWaitAsync(NpgsqlDataSource dataSource, string value, CancellationToken ct)
    {
        var client = new AppendFunctionClient(dataSource, _options.Schema);
        var e = JsonEvent(nameof(TestEvent), $"{{\"Value\": \"{value}\"}}");
        var append = client.AppendAsync([Any("s1", e)], ct);

        await CancelBackendAsync(dataSource, await WaitForSyncRepAsync(ct), ct);
        await append;
    }

    private static async Task CancelBackendAsync(NpgsqlDataSource dataSource, int pid, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT pg_cancel_backend($1)");
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pid });
        await command.ExecuteNonQueryAsync(ct);
    }

    // Counts the events that queries on the primary see.
    private async Task<long> CountEventsAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT count(*) FROM {new SchemaObjectNames(_options.Schema).EventLog}");

        return (long)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    // Waits until a backend that is creating a replication slot waits for the given transaction to end. The query finds
    // the backend through the slot, which it holds while it creates it.
    private static async Task WaitForXidLockWaitAsync(NpgsqlDataSource dataSource, string xid, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (SELECT 1
                           FROM pg_locks l
                           JOIN pg_replication_slots s ON s.active_pid = l.pid
                           WHERE l.locktype = 'transactionid' AND NOT l.granted AND l.transactionid::text = $1)
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

    // Waits until the backend's commit waits for the standby.
    private async Task WaitForSyncRepAsync(int pid, CancellationToken ct)
    {
        while (!await IsWaitingForSyncRepAsync(pid, ct))
            await Task.Delay(20, ct);
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

    // An append that calls append_events directly, and commits waiting for the standby to apply the event rather than
    // flush it.
    private sealed class RemoteApplyAppend(
        NpgsqlConnection connection,
        CancellationTokenSource cancellation,
        Task completion) :
        IAsyncDisposable
    {
        public int ProcessId { get; } = connection.ProcessID;

        public Task Completion { get; } = completion;

        public static async Task<RemoteApplyAppend> StartAsync(
            NpgsqlDataSource dataSource,
            string schema,
            string value,
            CancellationToken ct)
        {
            var connection = await dataSource.OpenConnectionAsync(ct);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var client = new AppendFunctionClient(dataSource, schema);

            return new RemoteApplyAppend(
                connection,
                cancellation,
                AppendAsync(client, connection, value, cancellation.Token));
        }

        // Cancels a commit that still waits for the standby, as when a test fails first, since closing the
        // connection mid-command would throw. The test observes the append's outcome through Completion.
        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            await Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            cancellation.Dispose();
            await connection.DisposeAsync();
        }

        private static async Task AppendAsync(
            AppendFunctionClient client,
            NpgsqlConnection connection,
            string value,
            CancellationToken ct)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);

            var e = JsonEvent(nameof(TestEvent), $"{{\"Value\": \"{value}\"}}");
            await client.AppendAsync(connection, [Any("s1", e)], ct);

            await using (var command = new NpgsqlCommand("SET LOCAL synchronous_commit = remote_apply", connection))
                await command.ExecuteNonQueryAsync(ct);

            await transaction.CommitAsync(ct);
        }
    }
}