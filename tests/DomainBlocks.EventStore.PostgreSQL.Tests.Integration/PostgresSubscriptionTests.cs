using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// PostgreSQL-specific subscription behaviour beyond the shared suite: slot sharing and stream-position resume.
/// </summary>
[TestFixture]
public class PostgresSubscriptionTests
{
    private const string Schema = "dbx_es_pg_subscription_tests";
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
    public async Task SubscribeToAll_TwoSubscribers_ShareOneSlotUntilTheLastUnsubscribes(CancellationToken ct)
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
    public async Task SubscribeToStream_WhenQueueOverflows_ResumesByStreamPosition(CancellationToken ct)
    {
        await using var enumerator = _eventStore
            .SubscribeToStream("target", SubscriptionOrigin.Start<StreamPosition>(), new SubscriptionOptions
            {
                QueueCapacity = 1
            })
            .GetAsyncEnumerator(ct);

        await ShouldBeCaughtUpAsync(enumerator);

        // Interleave the target stream with another one so that the resume position (a stream position) differs
        // from the global position.
        var expected = new List<TestEvent>();

        for (var i = 0; i < 40; i++)
        {
            var targetEvent = new TestEvent { Value = $"target-{i}" };
            expected.Add(targetEvent);

            await _eventStore.AppendAsync(
                "other",
                [Appendable(new TestEvent { Value = $"other-{i}" })],
                cancellationToken: ct);
            await _eventStore.AppendAsync("target", [Appendable(targetEvent)], cancellationToken: ct);
        }

        var observed = new List<ReadEvent<object, string, StreamPosition, LogPosition>>();
        var fellBehindCount = 0;
        var caughtUpCount = 0;

        while (fellBehindCount == 0 || caughtUpCount < fellBehindCount)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();

            switch (enumerator.Current)
            {
                case SubscriptionMessage.Event<ReadEvent<object, string, StreamPosition, LogPosition>> message:
                    observed.Add(message.Value);
                    break;
                case SubscriptionMessage.CaughtUp:
                    caughtUpCount++;
                    break;
                case SubscriptionMessage.FellBehind:
                    fellBehindCount++;
                    break;
            }
        }

        observed.Select(x => x.Payload).ShouldBe(expected);
        observed.ShouldAllBe(x => x.Context.StreamId == "target");
        observed.Select(x => x.Context.StreamPosition.Value).ShouldBe(Enumerable.Range(0, 40).Select(i => (ulong)i));
        fellBehindCount.ShouldBeGreaterThan(0);
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
