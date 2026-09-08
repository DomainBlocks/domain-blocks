using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Loss of the replication connection: the feed reconnects with a new temporary slot and subscriptions recover the
/// events committed in the meantime from their last position.
/// </summary>
[TestFixture]
public class PostgresFeedRecoveryTests
{
    private const string Schema = "dbx_es_feed_recovery_tests";
    private static readonly EventTypeMap EventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

    private static readonly PostgresEventStoreOptions Options = new()
    {
        Schema = Schema,
        Replication = { RetryDelay = TimeSpan.FromMilliseconds(100), MaxRetryDelay = TimeSpan.FromMilliseconds(500) }
    };

    private AppendFunctionClient _client = null!;
    private PostgresEventStore<object> _eventStore = null!;

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
    public async Task SetUp()
    {
        await _client.ResetAsync();

        _eventStore = PostgresEventStore.Create(
            SetUpFixture.DataSource,
            TestPostgresEventCodec.Create<object>(EventTypeMap),
            Options,
            SetUpFixture.LoggerFactory.CreateLogger("PostgresEventStore"));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventStore.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenWalSenderIsTerminated_ObservesEveryEventOnce(CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>())
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);

        var before = await AppendEventsAsync("before", 10, ct);
        var observed = new List<TestEvent>();

        for (var i = 0; i < before.Length; i++)
            observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        var oldSlot = await TerminateWalSenderAsync(ct);

        // Committed while the feed is down or reconnecting: never streamed by the new slot, so it must be recovered
        // by catch-up.
        var during = await AppendEventsAsync("during", 20, ct);

        var recovered = await ReadUntilRecoveredAsync(enumerator);
        observed.AddRange(recovered.Events);

        var after = new TestEvent { Value = "after" };
        await _eventStore.AppendAsync("s1", [Appendable(after)], cancellationToken: ct);
        observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        recovered.FellBehindCount.ShouldBeGreaterThan(0);
        observed.ShouldBe(before.Concat(during).Append(after));

        var slots = await GetSlotNamesAsync(ct);
        slots.ShouldHaveSingleItem().ShouldNotBe(oldSlot);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenWalSenderIsTerminatedWhileIdle_ReportsFellBehindThenCaughtUp(
        CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>())
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);
        await TerminateWalSenderAsync(ct);

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.ShouldBeOfType<SubscriptionMessage.FellBehind>();
        await ShouldBeCaughtUpAsync(enumerator);

        var live = new TestEvent { Value = "live" };
        await _eventStore.AppendAsync("s1", [Appendable(live)], cancellationToken: ct);
        (await NextEventAsync(enumerator)).Payload.ShouldBe(live);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_TwoSubscribers_BothRecoverAfterFeedLoss(CancellationToken ct)
    {
        await using var first = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>())
            .GetAsyncEnumerator(ct);

        await using var second = _eventStore
            .SubscribeToStream("s1", SubscriptionOrigin.Start<StreamPosition>())
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(first);
        await ShouldBeCaughtUpAsync(second);

        await TerminateWalSenderAsync(ct);
        var during = await AppendEventsAsync("during", 5, ct);

        var firstRecovered = await ReadUntilRecoveredAsync(first);
        var secondRecovered = await ReadUntilRecoveredAsync(second);

        firstRecovered.Events.ShouldBe(during);
        secondRecovered.Events.ShouldBe(during);

        var live = new TestEvent { Value = "live" };
        await _eventStore.AppendAsync("s1", [Appendable(live)], cancellationToken: ct);
        (await NextEventAsync(first)).Payload.ShouldBe(live);
        (await NextEventAsync(second)).Payload.ShouldBe(live);
    }

    private async Task<TestEvent[]> AppendEventsAsync(string prefix, int count, CancellationToken ct)
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"{prefix}-{i}" }).ToArray();

        foreach (var e in events)
            await _eventStore.AppendAsync("s1", [Appendable(e)], cancellationToken: ct);

        return events;
    }

    private static async Task<string> TerminateWalSenderAsync(CancellationToken ct)
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            "SELECT slot_name, pg_terminate_backend(active_pid) FROM pg_replication_slots " +
            "WHERE slot_name LIKE 'dbx_%' AND active_pid IS NOT NULL");

        await using var reader = await command.ExecuteReaderAsync(ct);

        (await reader.ReadAsync(ct)).ShouldBeTrue("Expected an active replication slot to terminate");
        reader.GetBoolean(1).ShouldBeTrue();

        return reader.GetString(0);
    }

    private static async Task<List<string>> GetSlotNamesAsync(CancellationToken ct)
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            "SELECT slot_name FROM pg_replication_slots WHERE slot_name LIKE 'dbx_%'");

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));

        return names;
    }

    private static AppendableEvent<object> Appendable(TestEvent e) => AppendableEvent.Create<object>(e);

    private static async Task<ReadEvent<object, string, StreamPosition, LogPosition>> NextEventAsync(
        IAsyncEnumerator<SubscriptionMessage> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        return enumerator.Current
            .ShouldBeOfType<SubscriptionMessage.Event<ReadEvent<object, string, StreamPosition, LogPosition>>>()
            .Value;
    }

    private static async Task ShouldBeCaughtUpAsync(IAsyncEnumerator<SubscriptionMessage> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.ShouldBeOfType<SubscriptionMessage.CaughtUp>();
    }

    private static async Task<RecoveredEvents> ReadUntilRecoveredAsync(IAsyncEnumerator<SubscriptionMessage> enumerator)
    {
        var events = new List<TestEvent>();
        var fellBehindCount = 0;
        var caughtUpCount = 0;

        while (fellBehindCount == 0 || caughtUpCount < fellBehindCount)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();

            switch (enumerator.Current)
            {
                case SubscriptionMessage.Event<ReadEvent<object, string, StreamPosition, LogPosition>> message:
                    events.Add(message.Value.Payload.ShouldBeOfType<TestEvent>());
                    break;
                case SubscriptionMessage.CaughtUp:
                    caughtUpCount++;
                    break;
                case SubscriptionMessage.FellBehind:
                    fellBehindCount++;
                    break;
            }
        }

        return new RecoveredEvents([.. events], fellBehindCount);
    }

    private sealed record RecoveredEvents(TestEvent[] Events, int FellBehindCount);
}
