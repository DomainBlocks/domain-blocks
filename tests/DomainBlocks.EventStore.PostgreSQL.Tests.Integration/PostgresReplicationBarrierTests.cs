using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// The replication barrier: subscriptions are given an event only once the synchronous standbys have it. These tests
/// run on a server of their own, because <c>synchronous_standby_names</c> applies to the whole server. Naming a standby
/// that does not exist holds every barrier, while events appended with <c>synchronous_commit = local</c> are visible on
/// the primary at once.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PostgresReplicationBarrierTests
{
    private const string MissingStandby = "missing_standby";
    private static readonly TimeSpan HeldFor = TimeSpan.FromSeconds(1);
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
    private NpgsqlDataSource _localCommitDataSource = null!;

    [OneTimeSetUp]
    public async Task StartServerAsync()
    {
        _server = await PostgresServer.StartAsync();
        _dataSource = CreateDataSource(_server.ConnectionString);

        // Appends through this data source commit without waiting for a standby, as an append whose wait was canceled
        // does, so they are visible on the primary before any standby has them.
        _localCommitDataSource = CreateDataSource(
            new NpgsqlConnectionStringBuilder(_server.ConnectionString) { Options = "-c synchronous_commit=local" }
                .ConnectionString);

        await PostgresEventStoreAdmin.EnsureInitializedAsync(_dataSource, _options);
    }

    [OneTimeTearDown]
    public async Task StopServerAsync()
    {
        await _localCommitDataSource.DisposeAsync();
        await _dataSource.DisposeAsync();
        await _server.DisposeAsync();
    }

    [SetUp]
    public async Task ResetAsync()
    {
        await SetStandbyNamesAsync(string.Empty);

        await ExecuteAsync(
            $"TRUNCATE {_options.Schema}.event_log; " +
            $"UPDATE {_options.Schema}.sequences SET next = 0 WHERE name = 'event_log'");
    }

    [TearDown]
    public Task ClearStandbyNamesAsync() => SetStandbyNamesAsync(string.Empty);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_LiveEventNotOnStandby_IsDeliveredOnceTheStandbyHasIt(CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(subscription);
        await SetStandbyNamesAsync(MissingStandby);

        var live = await AppendLocallyAsync("live", ct);
        var next = subscription.MoveNextAsync().AsTask();

        await Task.Delay(HeldFor, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription, live);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_FromEndWhenTheEndIsNotOnStandby_StartsOnceTheStandbyHasIt(CancellationToken ct)
    {
        await SetStandbyNamesAsync(MissingStandby);
        await AppendLocallyAsync("existing", ct);

        // The end position is where the subscription starts, so it must not be one that a failover could undo.
        await using var eventStore = CreateEventStore();
        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        var next = subscription.MoveNextAsync().AsTask();

        await Task.Delay(HeldFor, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        (await next).ShouldBeTrue();
        subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventNotOnStandbyWhenCatchingUp_IsReplayedOnceTheStandbyHasIt(
        CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        await using var subscription = await StartTailThenSubscribeAsync(
            eventStore,
            x => x.SubscribeToAll(SubscriptionOrigin.Start),
            ct);

        var next = subscription.Subscription.MoveNextAsync().AsTask();

        await Task.Delay(HeldFor, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription.Subscription, subscription.Existing);
        await ShouldBeCaughtUpAsync(subscription.Subscription);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_EventNotOnStandbyWhenCatchingUp_IsReplayedOnceTheStandbyHasIt(
        CancellationToken ct)
    {
        await using var eventStore = CreateEventStore();
        await using var subscription = await StartTailThenSubscribeAsync(
            eventStore,
            x => x.SubscribeToStream("s1", SubscriptionOrigin.Start),
            ct);

        var next = subscription.Subscription.MoveNextAsync().AsTask();

        await Task.Delay(HeldFor, ct);
        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription.Subscription, subscription.Existing);
        await ShouldBeCaughtUpAsync(subscription.Subscription);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_BarrierWaitCanceled_DeliversOnlyOnceTheStandbyHasIt(CancellationToken ct)
    {
        await InterruptBarrierAsync("pg_cancel_backend", ct);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_BarrierBackendTerminated_DeliversOnlyOnceTheStandbyHasIt(CancellationToken ct)
    {
        await InterruptBarrierAsync("pg_terminate_backend", ct);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_StandbyRequiredButNotConfigured_ReportsAnErrorAndDeliversNothing(
        CancellationToken ct)
    {
        var logger = new RecordingLogger();
        var options = new PostgresEventStoreOptions
        {
            Schema = _options.Schema,
            Tail = _options.Tail,
            RequireSynchronousStandby = true
        };

        await using var eventStore = CreateEventStore(options, logger);
        using var subscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(subscriptionCts.Token);
        await ShouldBeCaughtUpAsync(subscription);

        await AppendLocallyAsync("live", ct);
        var next = subscription.MoveNextAsync().AsTask();

        await Task.Delay(HeldFor, ct);

        next.IsCompleted.ShouldBeFalse();
        logger.Errors.ShouldContain(x => x.Contains("no synchronous standby is configured"));

        // The event is never delivered, so the read in progress is canceled before the subscription is disposed.
        await subscriptionCts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(next);
    }

    // Starts the tail with one subscription, appends an event that the standby does not have, and then subscribes
    // again, so that only the new subscription's catch-up stands between the event and the subscriber.
    private async Task<CatchingUp> StartTailThenSubscribeAsync(
        IEventStore<object, string, StreamPosition, LogPosition> eventStore,
        Func<IEventStore<object, string, StreamPosition, LogPosition>,
            IAsyncEnumerable<SubscriptionMessage<object, string, StreamPosition, LogPosition>>> subscribe,
        CancellationToken ct)
    {
        var running = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(running);
        await SetStandbyNamesAsync(MissingStandby);

        var existing = await AppendLocallyAsync("existing", ct);

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

        var live = await AppendLocallyAsync("live", ct);
        var next = subscription.MoveNextAsync().AsTask();

        var interrupted = await WaitForBarrierAsync(ct);
        await ExecuteAsync($"SELECT {interrupt}({interrupted.Pid})");
        await WaitForBarrierAsync(ct, after: interrupted);

        next.IsCompleted.ShouldBeFalse();

        await SetStandbyNamesAsync(string.Empty);

        await ShouldBeEventAsync(next, subscription, live);

        var after = await AppendLocallyAsync("after", ct);
        (await subscription.MoveNextAsync()).ShouldBeTrue();
        subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(after);
    }

    // Waits until a barrier waits for a standby to confirm it, and returns its session and when its transaction
    // started. A retry can run in the same session, so a later barrier is told apart by its start.
    private async Task<WaitingBarrier> WaitForBarrierAsync(CancellationToken ct, WaitingBarrier? after = null)
    {
        while (true)
        {
            await using var command = _dataSource.CreateCommand(
                "SELECT pid, xact_start FROM pg_stat_activity " +
                "WHERE wait_event = 'SyncRep' AND query LIKE '%dbx_barrier%' AND xact_start > $1");

            command.Parameters.Add(new NpgsqlParameter<DateTime>
            {
                TypedValue = after?.TransactionStart ?? DateTime.UnixEpoch
            });

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
                return new WaitingBarrier(reader.GetInt32(0), reader.GetDateTime(1));

            await reader.CloseAsync();
            await Task.Delay(20, ct);
        }
    }

    private IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        PostgresEventStoreOptions? options = null,
        ILogger? logger = null)
    {
        return CreateEventStore(_dataSource, options ?? _options, logger);
    }

    private static IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        NpgsqlDataSource dataSource,
        PostgresEventStoreOptions options,
        ILogger? logger = null)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(options)
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseLogger(logger ?? PostgresTestEnvironment.LoggerFactory.CreateLogger(
                nameof(PostgresReplicationBarrierTests)))
            .Build();
    }

    private async Task<TestEvent> AppendLocallyAsync(string value, CancellationToken ct)
    {
        await using var appender = CreateEventStore(_localCommitDataSource, _options);
        var e = new TestEvent { Value = value };
        await appender.AppendAsync("s1", [AppendableEvent.Create<object>(e)], cancellationToken: ct);

        return e;
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
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> subscription,
        TestEvent expected)
    {
        (await next).ShouldBeTrue();
        subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(expected);
    }

    private static async Task ShouldBeCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> subscription)
    {
        (await subscription.MoveNextAsync()).ShouldBeTrue();
        subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }

    // The running subscription is disposed last, after the one catching up, and is not read: the tail holds the event
    // back from it too, until the standby has it.
    private sealed record CatchingUp(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> Running,
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> Subscription,
        TestEvent Existing) :
        IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Subscription.DisposeAsync();
            await Running.DisposeAsync();
        }
    }

    private sealed record WaitingBarrier(int Pid, DateTime TransactionStart);

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _errors = [];

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_errors)
                    return [.. _errors];
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
                return;

            lock (_errors)
                _errors.Add(formatter(state, exception));
        }
    }
}