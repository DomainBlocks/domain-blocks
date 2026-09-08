using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Focused SubscribeToAll cases until SubscribeToStream lands and the shared subscription suite can be enabled.
/// </summary>
[TestFixture]
public class PostgresSubscribeToAllTests
{
    private const string Schema = "dbx_es_subscribe_all_tests";
    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema };
    private static readonly EventTypeMap EventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

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
    public async Task SubscribeToAll_FromStart_ReadsCatchUpThenLiveEvents(CancellationToken ct)
    {
        TestEvent[] events = [new() { Value = "catch-up-1" }, new() { Value = "catch-up-2" }];
        await _eventStore.AppendAsync("s1", events.Select(Appendable), cancellationToken: ct);

        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>())
            .GetAsyncEnumerator(ct);

        (await NextEventAsync(enumerator)).Payload.ShouldBe(events[0]);
        (await NextEventAsync(enumerator)).Payload.ShouldBe(events[1]);
        await ShouldBeCaughtUpAsync(enumerator);

        var live = new TestEvent { Value = "live" };
        await _eventStore.AppendAsync("s1", [Appendable(live)], cancellationToken: ct);

        var observed = await NextEventAsync(enumerator);
        observed.Payload.ShouldBe(live);
        observed.Context.LogPosition.ShouldBe(new LogPosition(2));
        observed.Context.StreamPosition.ShouldBe(new StreamPosition(2));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithEventsAppendedDuringCatchUp_ObservesEachEventOnce(CancellationToken ct)
    {
        var catchUpEvents = Enumerable.Range(0, 100).Select(i => new TestEvent { Value = $"catch-up-{i}" }).ToArray();
        await _eventStore.AppendAsync("s1", catchUpEvents.Select(Appendable), cancellationToken: ct);

        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>())
            .GetAsyncEnumerator(ct);

        var observed = new List<TestEvent> { (await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>() };

        var liveEvent = new TestEvent { Value = "appended-during-catch-up" };
        await _eventStore.AppendAsync("s1", [Appendable(liveEvent)], cancellationToken: ct);

        for (var i = 1; i < catchUpEvents.Length; i++)
            observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        await ShouldBeCaughtUpAsync(enumerator);
        observed.Add((await NextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        observed.ShouldBe(catchUpEvents.Append(liveEvent));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenQueueOverflows_ReportsFellBehindAndRecovers(CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>(), new SubscriptionOptions { QueueCapacity = 1 })
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);

        var expected = Enumerable.Range(0, 100).Select(i => new TestEvent { Value = $"overflow-{i}" }).ToArray();
        await _eventStore.AppendAsync("s1", expected.Select(Appendable), cancellationToken: ct);

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

        events.ShouldBe(expected);
        fellBehindCount.ShouldBeGreaterThan(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_TwoSubscribers_ShareOneSlotAndUnsubscribeIndependently(CancellationToken ct)
    {
        var first = _eventStore.SubscribeToAll(SubscriptionOrigin.Start<LogPosition>()).GetAsyncEnumerator(ct);

        await using var second = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>())
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(first);
        await ShouldBeCaughtUpAsync(second);
        (await CountSlotsAsync(ct)).ShouldBe(1);

        var firstLive = new TestEvent { Value = "first-live" };
        await _eventStore.AppendAsync("s1", [Appendable(firstLive)], cancellationToken: ct);
        (await NextEventAsync(first)).Payload.ShouldBe(firstLive);
        (await NextEventAsync(second)).Payload.ShouldBe(firstLive);

        await first.DisposeAsync();

        var secondLive = new TestEvent { Value = "second-live" };
        await _eventStore.AppendAsync("s2", [Appendable(secondLive)], cancellationToken: ct);
        (await NextEventAsync(second)).Payload.ShouldBe(secondLive);
        (await CountSlotsAsync(ct)).ShouldBe(1);

        await second.DisposeAsync();

        while (await CountSlotsAsync(ct) > 0)
            await Task.Delay(50, ct);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_CancellationBeforeEnumeration_CancelsPromptly(CancellationToken ct)
    {
        using var subscriptionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await subscriptionCancellation.CancelAsync();

        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start<LogPosition>())
            .GetAsyncEnumerator(subscriptionCancellation.Token);

        await enumerator.MoveNextAsync().AsTask().ShouldThrowAsync<OperationCanceledException>();
    }

    private static async Task<long> CountSlotsAsync(CancellationToken ct)
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            "SELECT count(*) FROM pg_replication_slots WHERE slot_name LIKE 'dbx_%'");

        return (long)(await command.ExecuteScalarAsync(ct))!;
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
}
