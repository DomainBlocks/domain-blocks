using System.Net;
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
/// Tests the store with a real synchronous standby, which the primary can cut off from its WAL.
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
    public async Task AppendAsync_SessionWithRemoteApply_WaitsForReplay(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource(x => x.Options = "-c synchronous_commit=remote_apply");
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.PauseWalReplayOnStandbyAsync();

        var append = eventStore.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "e" })],
            cancellationToken: ct);

        var pid = await WaitForSyncRepAsync(ct);

        // The standby flushes the event during the hold, so a commit with synchronous_commit on would complete.
        await Task.Delay(HoldDuration, ct);
        (await IsWaitingForSyncRepAsync(pid, ct)).ShouldBeTrue();
        append.IsCompleted.ShouldBeFalse();

        await _server.ResumeWalReplayOnStandbyAsync();
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
    public async Task SubscribeToAll_BarrierWaitCanceled_ReplaysEventOnceOnStandby(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.CutOffStandbyAsync();
        await AppendWithCanceledWaitAsync(dataSource, "e", ct);

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        var next = subscription.NextAsync();

        // The barrier's commit completes as the append's did, without the standby, so the catch-up reads the end again,
        // and its next barrier waits for the standby.
        await CancelBackendAsync(dataSource, await WaitForSyncRepAsync(ct), ct);

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await _server.ReconnectStandbyAsync();

        (await next).Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_LiveEventNotOnStandby_DeliversEventOnceOnStandby(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        await _server.CutOffStandbyAsync();
        await AppendWithCanceledWaitAsync(dataSource, "e", ct);
        var next = subscription.NextAsync();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await _server.ReconnectStandbyAsync();

        (await next).Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(2 * TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_FailoverLosesLiveEvent_ResumesWithoutLostEvent(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource(_server.FailoverConnectionString);
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);

        // The feed reports its progress often, which lets the standby's copy of the slot become usable, and reconnects
        // quickly after the failover.
        await using var eventStore = CreateEventStore(dataSource, new PostgresEventStoreOptions
        {
            Schema = _options.Schema,
            Replication =
            {
                WalReceiverStatusInterval = TimeSpan.FromMilliseconds(100),
                RetryDelay = TimeSpan.FromMilliseconds(100),
                MaxRetryDelay = TimeSpan.FromMilliseconds(500)
            }
        });

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        await eventStore.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "before" })],
            cancellationToken: ct);

        (await subscription.NextEventAsync()).ShouldBe(new TestEvent { Value = "before" });
        await WaitForSlotCopyAsync(ct);

        await _server.CutOffStandbyAsync();
        await AppendWithCanceledWaitAsync(dataSource, "lost", ct);
        var next = subscription.NextAsync();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        // Until the standby is promoted there is no primary, and the feed keeps trying to reconnect through the
        // failover endpoint. The promoted standby never had the event, so the next append reuses its position.
        await _server.CrashPrimaryAsync();
        await WaitForFailoverConnectionsAsync(2, ct);
        await _server.PromoteStandbyAsync();

        await eventStore.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "after" })],
            cancellationToken: ct);

        var e = (await next).Event.ShouldNotBeNull();
        e.Payload.ShouldBe(new TestEvent { Value = "after" });
        e.Context.LogPosition.ShouldBe(LogPosition.FromInt64(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EndpointReachesStandby_DeliversOnceEndpointReachesPrimary(CancellationToken ct)
    {
        var primary = new NpgsqlConnectionStringBuilder(_server.PrimaryConnectionString);
        var standby = new NpgsqlConnectionStringBuilder(_server.StandbyConnectionString);

        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var appender = CreateEventStore(dataSource);

        // The proxy stands in for an endpoint that still reaches a standby, as one can for a while after a failover.
        await using var proxy = new TcpProxy(new DnsEndPoint(standby.Host.ShouldNotBeNull(), standby.Port));

        await using var proxiedDataSource = CreateDataSource(x =>
        {
            x.Host = IPAddress.Loopback.ToString();
            x.Port = proxy.Port;
        });

        await using var eventStore = CreateEventStore(proxiedDataSource, new PostgresEventStoreOptions
        {
            Schema = _options.Schema,
            Replication =
            {
                RetryDelay = TimeSpan.FromMilliseconds(100),
                MaxRetryDelay = TimeSpan.FromMilliseconds(500)
            }
        });

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        var next = subscription.NextAsync();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        proxy.MoveTo(new DnsEndPoint(primary.Host.ShouldNotBeNull(), primary.Port));

        (await next).IsCaughtUp.ShouldBeTrue();

        await appender.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "e" })],
            cancellationToken: ct);

        (await subscription.NextEventAsync()).ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_FromEndThroughStandby_PinsEndOnceEndpointReachesPrimary(CancellationToken ct)
    {
        var primary = new NpgsqlConnectionStringBuilder(_server.PrimaryConnectionString);
        var standby = new NpgsqlConnectionStringBuilder(_server.StandbyConnectionString);

        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var appender = CreateEventStore(dataSource);

        // The barrier after a read of the end runs only when the log has events and synchronous standbys are listed. A
        // standby then fails it, since a server in recovery can't write WAL.
        await appender.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "before" })],
            cancellationToken: ct);

        await _server.SetSynchronousStandbyNamesOnStandbyAsync();
        await using var standbyDataSource = NpgsqlDataSource.Create(_server.StandbyConnectionString);

        while (await CountEventsAsync(standbyDataSource, ct) == 0)
            await Task.Delay(20, ct);

        // The proxy stands in for an endpoint that still reaches a standby, as one can for a while after a failover.
        await using var proxy = new TcpProxy(new DnsEndPoint(standby.Host.ShouldNotBeNull(), standby.Port));

        await using var proxiedDataSource = CreateDataSource(x =>
        {
            x.Host = IPAddress.Loopback.ToString();
            x.Port = proxy.Port;
        });

        await using var eventStore = CreateEventStore(proxiedDataSource, new PostgresEventStoreOptions
        {
            Schema = _options.Schema,
            Replication =
            {
                RetryDelay = TimeSpan.FromMilliseconds(100),
                MaxRetryDelay = TimeSpan.FromMilliseconds(500)
            }
        });

        // The feed can't connect through the standby, so the subscription reads the end once the endpoint reaches the
        // primary.
        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.End).Probe(ct);
        var next = subscription.NextAsync();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        proxy.MoveTo(new DnsEndPoint(primary.Host.ShouldNotBeNull(), primary.Port));

        (await next).IsCaughtUp.ShouldBeTrue();

        await appender.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "after" })],
            cancellationToken: ct);

        (await subscription.NextEventAsync()).ShouldBe(new TestEvent { Value = "after" });
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_SynchronizedStandbySlotsEmpty_DeliversEventNotOnStandby(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.ClearSynchronizedStandbySlotsAsync();

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        await _server.CutOffStandbyAsync();
        await AppendWithCanceledWaitAsync(dataSource, "e", ct);

        (await subscription.NextEventAsync()).ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(2 * TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_OtherWritesWhileIdle_StandbyCopyBecomesUsable(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);

        await using var eventStore = CreateEventStore(dataSource, new PostgresEventStoreOptions
        {
            Schema = _options.Schema,
            Replication = { WalReceiverStatusInterval = TimeSpan.FromMilliseconds(100) }
        });

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).Probe(ct);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        // With no events, only the keepalives' position can move the slot. Logging the running transactions here saves
        // waiting for the primary to log them.
        await using var create = dataSource.CreateCommand("CREATE TABLE other (value bigint)");
        await create.ExecuteNonQueryAsync(ct);

        await using var write = dataSource.CreateCommand(
            "INSERT INTO other VALUES (1); SELECT pg_log_standby_snapshot()");

        var copied = WaitForSlotCopyAsync(ct);

        while (!copied.IsCompleted)
        {
            await write.ExecuteNonQueryAsync(ct);
            await Task.WhenAny(copied, Task.Delay(200, ct));
        }

        await copied;
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

    // Returns once the given number of further connections have reached the failover endpoint.
    private async Task WaitForFailoverConnectionsAsync(int count, CancellationToken ct)
    {
        var target = _server.FailoverConnectionCount + count;

        while (_server.FailoverConnectionCount < target)
            await Task.Delay(20, ct);
    }

    // The standby's copy of a new slot stays temporary, and a promotion drops it, until the slot advances past a record
    // of the running transactions, which the primary logs every 15 seconds.
    private async Task WaitForSlotCopyAsync(CancellationToken ct)
    {
        await using var standby = NpgsqlDataSource.Create(_server.StandbyConnectionString);

        await using var isCopied = standby.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_replication_slots " +
            "WHERE slot_type = 'logical' AND synced AND NOT temporary)");

        while (!(bool)(await isCopied.ExecuteScalarAsync(ct)).ShouldNotBeNull())
            await Task.Delay(100, ct);
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

    private EventStore CreateEventStore(NpgsqlDataSource dataSource, PostgresEventStoreOptions? options = null)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(options ?? _options)
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseLogger(PostgresTestEnvironment.LoggerFactory.CreateLogger(nameof(PostgresSynchronousStandbyTests)))
            .Build();
    }

    private NpgsqlDataSource CreateDataSource(Action<NpgsqlConnectionStringBuilder>? configure = null)
    {
        return CreateDataSource(_server.PrimaryConnectionString, configure);
    }

    private NpgsqlDataSource CreateDataSource(
        string serverConnectionString,
        Action<NpgsqlConnectionStringBuilder>? configure = null)
    {
        // The feed's replication connection uses this connection string, so it keeps the password.
        var connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
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