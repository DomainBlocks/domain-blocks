using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// How a subscription replays the log when it has to restart. The subscription is built from its parts, as the store
/// builds it, so that a witness on the same feed can tell when the feed has given it all the appended events.
/// </summary>
[TestFixture]
public class PostgresSubscriptionCatchUpTests : PostgresIntegrationTest
{
    private IEventStore<object, string, StreamPosition, LogPosition> _eventStore = null!;
    private RefCountedFeed<EventLogRow<object>, long> _feed = null!;
    private EventLogReader<object> _reader = null!;

    [SetUp]
    public void SetUp()
    {
        var codec = EventCodec.Create(new EventCodecOptions<object, PostgresEventData, string>
        {
            TypeMap = DefaultEventTypeMap,
            EventSerializer = new JsonObjectSerializer().AsPostgresEventDataSerializer(),
            MetadataSerializer = new JsonMetadataSerializer()
        });

        var names = new SchemaObjectNames(Schema);

        _eventStore = CreateEventStore();
        _reader = new EventLogReader<object>(DataSource, new EventLogSql(names), Options.ReadPageSize, codec);

        var slotNameGenerator = new SlotNameGenerator(names);

        var resumePolicy = new FeedResumePolicy(EventLogFeedTransientErrors.IsTransient);

        _feed = new RefCountedFeed<EventLogRow<object>, long>(() =>
            new ReplicationEventLogFeed<object>(
                PostgresTestEnvironment.ConnectionString,
                DataSource,
                slotNameGenerator,
                names,
                Options.Replication,
                codec,
                resumePolicy,
                LoggerFactory.CreateLogger("ReplicationEventLogFeed")));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _feed.DisposeAsync();
        await _eventStore.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenQueueOverflowsDuringReplay_FinishesReplayBeforeRestarting(CancellationToken ct)
    {
        var existing = await AppendEventsAsync("existing", 3, ct);

        await using var subscription = SubscribeToAll(new SubscriptionOptions { QueueCapacity = 1 }).Probe(ct);

        // The replay delivers its first event, and the subscriber leaves the rest until later.
        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        await using var witness = await CreateWitnessAsync(ct);

        // More events arrive live than the queue holds, so the subscription has to restart.
        var live = await AppendEventsAsync("live", 2, ct);

        await ReadEventsAsync(witness, live.Length);

        var messages = await subscription.ReadUntilCaughtUpAsync();

        // The subscriber had not been told that it caught up, so it is not told that it fell behind.
        messages.Select(x => x.Kind).ShouldBe(
        [
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.CaughtUp
        ]);

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([existing[1], existing[2], live[0], live[1]]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenQueueOverflowsAgainBeforeCatchingUpAgain_ReportsFellBehindOnce(
        CancellationToken ct)
    {
        var existing = await AppendEventsAsync("existing", 1, ct);

        await using var subscription = SubscribeToAll(new SubscriptionOptions { QueueCapacity = 1 }).Probe(ct);

        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        // The queue overflows while the subscriber is caught up, so it is told that it fell behind.
        TestEvent[] first;

        await using (var witness = await CreateWitnessAsync(ct))
        {
            first = await AppendEventsAsync("first", 3, ct);
            await ReadEventsAsync(witness, first.Length);
        }

        (await subscription.NextEventAsync()).ShouldBe(first[0]);
        (await subscription.NextAsync()).IsFellBehind.ShouldBeTrue();

        // The replay that follows delivers its first event, and the subscriber leaves the rest until later.
        (await subscription.NextEventAsync()).ShouldBe(first[1]);

        // The queue overflows again before the subscriber has caught up again. The subscription attached to the feed
        // again when it restarted, so it takes another witness to follow it.
        await using var secondWitness = await CreateWitnessAsync(ct);
        var second = await AppendEventsAsync("second", 2, ct);
        await ReadEventsAsync(secondWitness, second.Length);

        var messages = await subscription.ReadUntilCaughtUpAsync();

        // It took a third replay to catch up, and the subscriber was not told a second time that it fell behind.
        messages.Select(x => x.Kind).ShouldBe(
        [
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.CaughtUp
        ]);

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([first[2], second[0], second[1]]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenRestartingAfterFilteredCatchUp_ResumesFromLastObservedPosition(
        CancellationToken ct)
    {
        // The filter selects the first of the existing events and not the two that follow it.
        var selected = await AppendEventsAsync("existing", 1, ct, "selected");
        await AppendEventsAsync("existing", 2, ct, "excluded");

        var options = new SubscriptionOptions { Filter = EventFilter.StreamIds("selected"), QueueCapacity = 1 };
        await using var subscription = SubscribeToAll(options).Probe(ct);

        // The replay delivers the event that the filter selects, and the subscriber leaves the rest until later.
        (await subscription.NextEventAsync()).ShouldBe(selected[0]);
        await using var witness = await CreateWitnessAsync(ct);

        // More events that the filter selects arrive live than the queue holds, so the subscription has to restart.
        var live = await AppendEventsAsync("live", 2, ct, "selected");
        await ReadEventsAsync(witness, live.Length);

        var messages = await subscription.ReadUntilCaughtUpAsync();

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([live[0], live[1]]);
    }

    // The subscription under test, from the start of the log.
    private SubscriptionAsyncEnumerable<object, EventLogRow<object>, long, LogPosition> SubscribeToAll(
        SubscriptionOptions options)
    {
        return SubscribeToAll(SubscriptionOrigin.Start, options);
    }

    // A second subscription to the same feed, from the end of the log, used to tell when the feed has given all the
    // appended events to the subscription under test. The feed gives each event to its subscriptions in the order in
    // which they attached to it, so a witness is created after the subscription under test has attached.
    private async Task<SubscriptionProbe<StreamPosition, LogPosition>> CreateWitnessAsync(CancellationToken ct)
    {
        var witness = SubscribeToAll(SubscriptionOrigin.End, new SubscriptionOptions()).Probe(ct);

        (await witness.NextAsync()).IsCaughtUp.ShouldBeTrue();

        return witness;
    }

    private SubscriptionAsyncEnumerable<object, EventLogRow<object>, long, LogPosition> SubscribeToAll(
        SubscriptionOrigin<LogPosition> origin,
        SubscriptionOptions options)
    {
        var correlationId = CorrelationId.ReserveOrGenerate(options.CorrelationId);
        var logger = LoggerFactory.CreateLogger("Subscription");

        return new SubscriptionAsyncEnumerable<object, EventLogRow<object>, long, LogPosition>(
            _feed,
            new PostgresAllCatchUpReader<object>(_reader, options.Filter, options, correlationId, logger),
            options.Filter,
            origin,
            options.QueueCapacity,
            correlationId,
            logger);
    }

    private async Task<TestEvent[]> AppendEventsAsync(
        string prefix,
        int count,
        CancellationToken ct,
        string streamId = "s1")
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"{prefix}-{i}" }).ToArray();

        foreach (var e in events)
            await _eventStore.AppendAsync(streamId, [Appendable(e)], cancellationToken: ct);

        return events;
    }

    private static async Task ReadEventsAsync(SubscriptionProbe<StreamPosition, LogPosition> witness, int count)
    {
        for (var i = 0; i < count; i++)
            await witness.NextAsync();
    }
}