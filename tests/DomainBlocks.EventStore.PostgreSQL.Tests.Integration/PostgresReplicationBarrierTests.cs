using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

using EventStore = IEventStore<object, string, StreamPosition, LogPosition>;
using SubscriptionMessage = SubscriptionMessage<object, string, StreamPosition, LogPosition>;

/// <summary>
/// Durability with synchronous standbys: an append is acknowledged and visible only once the standbys have it, and
/// subscriptions are given an event only once the standbys have it, even when the event became visible first. These
/// tests run on a server of their own, because <c>synchronous_standby_names</c> applies to the whole server. Naming a
/// standby that does not exist holds every commit that waits for one, and canceling an append's wait makes its event
/// visible on the primary while no standby has it. Clearing the setting then stands in for the standby: PostgreSQL
/// releases the waiting commits as it would once a standby confirmed them, and they complete without a warning.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PostgresReplicationBarrierTests
{
    private const string MissingStandby = "missing_standby";
    private const string AppendQuery = "append_events";
    // The barrier is told apart by its message, which is quoted because the schema shares the barrier's name.
    private const string BarrierQuery = "'dbx_barrier'";

    // Only the barrier after a read of the log's end has no condition beyond the rows existing.
    private const string EndBarrierQuery = "WHERE true)";
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(1);
    private static readonly EventTypeMap EventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    private readonly PostgresEventStoreOptions _options = new()
    {
        Schema = "dbx_barrier",
        Tail = new EventLogTailOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(200),
            RetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(200)
        }
    };

    private PostgresServer _server = null!;
    private NpgsqlDataSource _dataSource = null!;
    private NpgsqlDataSource _asyncCommitDataSource = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _server = await PostgresServer.StartAsync();
        _dataSource = CreateDataSource(_server.ConnectionString);

        // Sessions from this data source ask for commits that wait for nothing.
        var asyncCommit = new NpgsqlConnectionStringBuilder(_server.ConnectionString)
        {
            Options = "-c synchronous_commit=off"
        };

        _asyncCommitDataSource = CreateDataSource(asyncCommit.ConnectionString);

        await PostgresEventStoreAdmin.EnsureInitializedAsync(_dataSource, _options);
        await ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pg_walinspect");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _asyncCommitDataSource.DisposeAsync();
        await _dataSource.DisposeAsync();
        await _server.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await SetStandbyNamesAsync(string.Empty);

        await ExecuteAsync(
            $"TRUNCATE {_options.Schema}.event_log; " +
            $"UPDATE {_options.Schema}.sequences SET next = 0 WHERE name = 'event_log'");
    }

    [TearDown]
    public Task TearDown() => SetStandbyNamesAsync(string.Empty);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SessionAsksForAsynchronousCommit_WaitsForTheStandby(CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);

        await using var eventStore = CreateEventStore(_asyncCommitDataSource);
        var e = new TestEvent { Value = "async" };
        var append = eventStore.AppendAsync("s1", [AppendableEvent.Create<object>(e)], cancellationToken: ct);

        await Task.Delay(HoldDuration, ct);
        append.IsCompleted.ShouldBeFalse();
        (await CountEventsAsync()).ShouldBe(0);

        await SetStandbyNamesAsync(string.Empty);

        await append;
        (await CountEventsAsync()).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ExecuteAsync_ResultsOutgrowTheServersOutputBuffer_CompletesNothingUntilTheStandbyHasTheCommit(
        CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);

        var batch = CreateBatch(2_000);
        using var command = new AppendBatchCommand(_dataSource, new SchemaObjectNames(_options.Schema));
        var execute = command.ExecuteAsync(batch, ct);

        await WaitForSyncRepAsync(AppendQuery, ct);
        await Task.Delay(HoldDuration, ct);
        batch.Count(x => x.IsCompleted).ShouldBe(0);

        await SetStandbyNamesAsync(string.Empty);

        await execute;
        batch.ShouldAllBe(x => x.Completion.IsCompletedSuccessfully);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ExecuteAsync_BackendTerminatedWhileTheCommitWaits_CompletesNothing(CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);

        var batch = CreateBatch(2_000);
        using var command = new AppendBatchCommand(_dataSource, new SchemaObjectNames(_options.Schema));
        var execute = command.ExecuteAsync(batch, ct);

        var waiting = await WaitForSyncRepAsync(AppendQuery, ct);
        await ExecuteAsync($"SELECT pg_terminate_backend({waiting.Pid})");

        // The commit stands on the primary, but the client cannot know that, so no request is reported as appended.
        await Should.ThrowAsync<NpgsqlException>(execute);
        batch.ShouldAllBe(x => !x.IsCompleted);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ExecuteAsync_CommitWaitCanceled_ThrowsAndCompletesNothing(CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);

        var batch = CreateBatch(1);
        using var command = new AppendBatchCommand(_dataSource, new SchemaObjectNames(_options.Schema));
        var execute = command.ExecuteAsync(batch, ct);

        var waiting = await WaitForSyncRepAsync(AppendQuery, ct);
        await ExecuteAsync($"SELECT pg_cancel_backend({waiting.Pid})");

        // The commit stands on the primary, with only a warning, but no standby has it.
        await Should.ThrowAsync<CommitNotConfirmedException>(execute);
        batch.ShouldAllBe(x => !x.IsCompleted);
        (await CountEventsAsync()).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task IsUnconfirmedCommit_WarningOfACanceledWait_ReturnsTrue(CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var notices = new List<PostgresNotice>();
        connection.Notice += (_, e) => notices.Add(e.Notice);

        await using var command = new NpgsqlCommand(
            "SELECT set_config('synchronous_commit', 'on', true), pg_logical_emit_message(true, 'dbx_probe', '')",
            connection);

        var commit = command.ExecuteNonQueryAsync(ct);
        var waiting = await WaitForSyncRepAsync("'dbx_probe'", ct);
        await ExecuteAsync($"SELECT pg_cancel_backend({waiting.Pid})");
        await commit;

        // The server reports the function that raised the warning, which is how it is recognized.
        var warning = notices.ShouldHaveSingleItem();
        warning.Routine.ShouldBe("SyncRepWaitForLSN");
        CommitNotices.IsUnconfirmedCommit(warning).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_TriggerRaisesWarning_Appends(CancellationToken ct)
    {
        var table = $"{_options.Schema}.event_log";

        await ExecuteAsync(
            $"CREATE FUNCTION {_options.Schema}.warn() RETURNS trigger LANGUAGE plpgsql AS " +
            "$$ BEGIN RAISE WARNING 'audited'; RETURN NULL; END $$; " +
            $"CREATE TRIGGER warn AFTER INSERT ON {table} " +
            $"FOR EACH STATEMENT EXECUTE FUNCTION {_options.Schema}.warn()");

        try
        {
            // The warning does not come from the commit's wait for the standbys, so it is ignored.
            var e = await AppendAsync("audited", ct);

            (await CountEventsAsync()).ShouldBe(1);
            e.Value.ShouldBe("audited");
        }
        finally
        {
            await ExecuteAsync($"DROP TRIGGER warn ON {table}; DROP FUNCTION {_options.Schema}.warn()");
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_CommitWaitCanceled_AcknowledgesOnlyOnceCommitConfirmed(CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);

        await using var eventStore = CreateEventStore();
        var e = new TestEvent { Value = "canceled" };
        var append = eventStore.AppendAsync("s1", [AppendableEvent.Create<object>(e)], cancellationToken: ct);

        var waiting = await WaitForSyncRepAsync(AppendQuery, ct);
        await ExecuteAsync($"SELECT pg_cancel_backend({waiting.Pid})");

        // The appender runs the batch again. It finds the append already committed, and its own commit waits for the
        // standby, which then has the append too.
        await WaitForSyncRepAsync(AppendQuery, ct, after: waiting);
        append.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await append;
        (await CountEventsAsync()).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_NoSynchronousStandby_WritesNoBarrier(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        var appended = new[] { await AppendAsync("first", ct), await AppendAsync("second", ct) };
        var start = await GetWalPositionAsync();

        // The tail's start, the catch-up's end and the catch-up's replay all read events that exist.
        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);

        foreach (var expected in appended)
            await ShouldBeEventAsync(subscription.MoveNextAsync().AsTask(), subscription, expected);

        await ShouldBeCaughtUpAsync(subscription);
        (await CountBarriersSinceAsync(start)).ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_LiveEventNotOnStandby_IsDeliveredOnceBarrierConfirmed(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(subscription);
        await SetStandbyNamesAsync(MissingStandby);

        var live = await AppendWithCanceledWaitAsync("live", ct);
        var next = subscription.MoveNextAsync().AsTask();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription, live);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_FromEndWhenTheEndIsNotOnStandby_StartsOnceBarrierConfirmed(CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);
        await AppendWithCanceledWaitAsync("existing", ct);

        // The end position is where the subscription starts, so it must not be one that a failover could undo.
        await using var eventStore = CreateEventStore();
        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        var next = subscription.MoveNextAsync().AsTask();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        (await next).ShouldBeTrue();
        subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventNotOnStandbyWhenCatchingUp_IsReplayedOnceBarrierConfirmed(
        CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();

        await using var subscription = await StartTailThenSubscribeAsync(
            eventStore,
            x => x.SubscribeToAll(SubscriptionOrigin.Start),
            ct);

        var next = subscription.Subscription.MoveNextAsync().AsTask();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription.Subscription, subscription.Existing);
        await ShouldBeCaughtUpAsync(subscription.Subscription);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_EventNotOnStandbyWhenCatchingUp_IsReplayedOnceBarrierConfirmed(
        CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();

        await using var subscription = await StartTailThenSubscribeAsync(
            eventStore,
            x => x.SubscribeToStream("s1", SubscriptionOrigin.Start),
            ct);

        var next = subscription.Subscription.MoveNextAsync().AsTask();

        await Task.Delay(HoldDuration, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription.Subscription, subscription.Existing);
        await ShouldBeCaughtUpAsync(subscription.Subscription);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_BarrierWaitCanceled_DeliversOnlyOnceBarrierConfirmed(CancellationToken ct)
    {
        await InterruptBarrierAsync("pg_cancel_backend", ct);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_BarrierBackendTerminated_DeliversOnlyOnceBarrierConfirmed(CancellationToken ct)
    {
        await InterruptBarrierAsync("pg_terminate_backend", ct);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_CatchUpEndBarrierBackendTerminated_ReplaysOnceBarrierConfirmed(
        CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();

        await using var subscription = await StartTailThenSubscribeAsync(
            eventStore,
            x => x.SubscribeToAll(SubscriptionOrigin.Start),
            ct);

        var next = subscription.Subscription.MoveNextAsync().AsTask();

        // The tail started before the standby went missing, so the catch-up's end read is the one that waits here. Its
        // connection is lost, so the catch-up reads its end again.
        var interrupted = await WaitForSyncRepAsync(EndBarrierQuery, ct);
        await ExecuteAsync($"SELECT pg_terminate_backend({interrupted.Pid})");
        await WaitForSyncRepAsync(EndBarrierQuery, ct, after: interrupted);

        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription.Subscription, subscription.Existing);
        await ShouldBeCaughtUpAsync(subscription.Subscription);
    }

    // Starts the tail with one subscription, appends an event that the standby does not have, and then subscribes
    // again, so that only the new subscription's catch-up stands between the event and the subscriber.
    private async Task<CatchingUp> StartTailThenSubscribeAsync(
        EventStore eventStore,
        Func<EventStore, IAsyncEnumerable<SubscriptionMessage>> subscribe,
        CancellationToken ct)
    {
        var running = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(running);
        await SetStandbyNamesAsync(MissingStandby);

        var existing = await AppendWithCanceledWaitAsync("existing", ct);

        return new CatchingUp(running, subscribe(eventStore).GetAsyncEnumerator(ct), existing);
    }

    // A barrier that is interrupted while it waits must not count as confirmed. The tail reads again, waits again, and
    // delivers the event once, when the standby has it.
    private async Task InterruptBarrierAsync(string interrupt, CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(subscription);
        await SetStandbyNamesAsync(MissingStandby);

        var live = await AppendWithCanceledWaitAsync("live", ct);
        var next = subscription.MoveNextAsync().AsTask();

        var interrupted = await WaitForSyncRepAsync(BarrierQuery, ct);
        await ExecuteAsync($"SELECT {interrupt}({interrupted.Pid})");
        await WaitForSyncRepAsync(BarrierQuery, ct, after: interrupted);

        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription, live);

        var after = await AppendAsync("after", ct);
        (await subscription.MoveNextAsync()).ShouldBeTrue();
        subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(after);
    }

    // Cancels the wait of an append's commit for the standby, as a client's cancel request or pg_cancel_backend does.
    // The commit then stands on the primary, and the event is visible there, although no standby has it. The appender
    // runs the batch again, which waits for the standby too, and is disposed while it waits, so the append fails.
    private async Task<TestEvent> AppendWithCanceledWaitAsync(string value, CancellationToken ct)
    {
        var e = new TestEvent { Value = value };
        Task append;

        await using (var appender = CreateEventStore())
        {
            append = appender.AppendAsync("s1", [AppendableEvent.Create<object>(e)], cancellationToken: ct);

            var waiting = await WaitForSyncRepAsync(AppendQuery, ct);
            await ExecuteAsync($"SELECT pg_cancel_backend({waiting.Pid})");
            await WaitForSyncRepAsync(AppendQuery, ct, after: waiting);
        }

        await Task.WhenAny(append);
        append.IsFaulted.ShouldBeTrue();

        return e;
    }

    // The server buffers its output up to 8 KB, so the result rows of a batch of 2,000 requests reach the client before
    // the commit has waited for the standby.
    private static AppendRequest[] CreateBatch(int count)
    {
        return Enumerable
            .Range(0, count)
            .Select(i => new AppendRequest(
                $"large-{i}",
                ExpectedStreamState.Any<StreamPosition>(),
                Guid.NewGuid(),
                [EncodedEvent.Create<PostgresEventData, string>("TestEvent", PostgresEventData.FromJson("{}"), null)]))
            .ToArray();
    }

    private async Task<TestEvent> AppendAsync(string value, CancellationToken ct)
    {
        await using var appender = CreateEventStore();
        var e = new TestEvent { Value = value };
        await appender.AppendAsync("s1", [AppendableEvent.Create<object>(e)], cancellationToken: ct);

        return e;
    }

    // Waits until a commit whose query contains the given text waits for a standby to confirm it, and returns its
    // session and when its transaction started. A retry can run in the same session, so a later wait is told apart by
    // its start.
    private async Task<WaitingCommit> WaitForSyncRepAsync(
        string query,
        CancellationToken ct,
        WaitingCommit? after = null)
    {
        while (true)
        {
            await using var command = _dataSource.CreateCommand(
                "SELECT pid, xact_start FROM pg_stat_activity " +
                "WHERE wait_event = 'SyncRep' AND strpos(query, $1) > 0 AND xact_start > $2");

            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = query });
            command.Parameters.Add(new NpgsqlParameter<DateTime>
            {
                TypedValue = after?.TransactionStart ?? DateTime.UnixEpoch
            });

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
                return new WaitingCommit(reader.GetInt32(0), reader.GetDateTime(1));

            await reader.CloseAsync();
            await Task.Delay(20, ct);
        }
    }

    private async Task<long> CountEventsAsync()
    {
        await using var command = _dataSource.CreateCommand($"SELECT count(*) FROM {_options.Schema}.event_log");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> GetWalPositionAsync()
    {
        await using var command = _dataSource.CreateCommand("SELECT pg_current_wal_lsn()::text");
        return (string)(await command.ExecuteScalarAsync())!;
    }

    // Counts the logical messages written to the WAL since the given position, which only the barrier writes.
    private async Task<long> CountBarriersSinceAsync(string start)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT CASE
                WHEN pg_current_wal_lsn() > $1::pg_lsn THEN (
                    SELECT count(*)
                    FROM pg_get_wal_records_info($1::pg_lsn, pg_current_wal_lsn())
                    WHERE resource_manager = 'LogicalMessage')
                ELSE 0
            END
            """);

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = start });

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private EventStore CreateEventStore(NpgsqlDataSource? dataSource = null)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource ?? _dataSource)
            .UseOptions(_options)
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseLogger(PostgresTestEnvironment.LoggerFactory.CreateLogger(nameof(PostgresReplicationBarrierTests)))
            .Build();
    }

    private NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UsePostgresEventStore(_options);
        return builder.Build();
    }

    // The setting applies to the whole server, and takes effect in each session when it next reloads its configuration.
    private async Task SetStandbyNamesAsync(string names)
    {
        await ExecuteAsync($"ALTER SYSTEM SET synchronous_standby_names = '{names}'");
        await ExecuteAsync("SELECT pg_reload_conf()");

        while (true)
        {
            await using var command = _dataSource.CreateCommand("SHOW synchronous_standby_names");

            if ((string?)await command.ExecuteScalarAsync() == names)
                return;

            await Task.Delay(20);
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ShouldBeEventAsync(
        Task<bool> next,
        IAsyncEnumerator<SubscriptionMessage> subscription,
        TestEvent expected)
    {
        (await next).ShouldBeTrue();
        subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(expected);
    }

    private static async Task ShouldBeCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage> subscription)
    {
        (await subscription.MoveNextAsync()).ShouldBeTrue();
        subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }

    // The running subscription is disposed last, after the one catching up, and is not read: the tail holds the event
    // back from it too, until the standby has it.
    private sealed record CatchingUp(
        IAsyncEnumerator<SubscriptionMessage> Running,
        IAsyncEnumerator<SubscriptionMessage> Subscription,
        TestEvent Existing) :
        IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Subscription.DisposeAsync();
            await Running.DisposeAsync();
        }
    }

    private sealed record WaitingCommit(int Pid, DateTime TransactionStart);
}