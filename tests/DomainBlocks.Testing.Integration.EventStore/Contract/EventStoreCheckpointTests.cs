using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// A subscription with a filter says how far it has looked, so that a consumer can resume past the events it was not
/// given. A checkpoint is the position to resume after: nothing the filter selects is at or before it undelivered.
/// </summary>
public abstract class EventStoreCheckpointTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : struct, IPosition<TStreamPos>
    where TLogPos : struct, IPosition<TLogPos>
{
    private static readonly EventFilter Orders = EventFilter.EventName(nameof(OrderPlaced));

    private static readonly SubscriptionOptions OrdersOften = new()
    {
        Filter = Orders,
        CheckpointInterval = TimeSpan.FromMilliseconds(20)
    };

    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

    protected override bool ResetLogBeforeEachTest => true;

    [SetUp]
    public void SetUp() => EventStore = CreateEventStore(EventFilterTestLog.TypeMap);

    [TearDown]
    public async Task TearDown()
    {
        if (EventStore is { } eventStore)
            await eventStore.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenCatchUpLooksPastTheLastEventItDelivers_ReportsHowFar(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("s1", ct, Order(1), Invoice(1), Invoice(2));

        var messages = await ReadUntilCaughtUpAsync(OrdersFromStart(), ct);

        // The order, then a checkpoint at the end of the log, which is two events on.
        messages.Count.ShouldBe(3);
        TotalOf(messages[0]).ShouldBe(1);
        messages[1].LogCheckpoint.Value.ShouldBe(CreateLogPosition(2));
        messages[1].StreamCheckpoint.HasValue.ShouldBeFalse();
        messages[2].IsCaughtUp.ShouldBeTrue();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenCatchUpDeliversNothing_StillReportsHowFar(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("s1", ct, Invoice(1), Invoice(2));

        var messages = await ReadUntilCaughtUpAsync(OrdersFromStart(), ct);

        messages.Count.ShouldBe(2);
        messages[0].LogCheckpoint.Value.ShouldBe(CreateLogPosition(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenTheLastEventOfTheLogIsDelivered_HasNothingMoreToReport(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("s1", ct, Invoice(1), Order(1));

        var messages = await ReadUntilCaughtUpAsync(OrdersFromStart(), ct);

        messages.Count(x => x.IsCheckpoint).ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenResumedAfterWhereTheLogEnds_HasNothingMoreToReport(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("s1", ct, Invoice(1), Invoice(2));

        var origin = SubscriptionOrigin.After(CreateLogPosition(1));
        var messages = await ReadUntilCaughtUpAsync(EventStore.SubscribeToAll(origin, OrdersOften), ct);

        messages.Count(x => x.IsCheckpoint).ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithoutAFilter_ReportsNoCheckpoints(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("s1", ct, Order(1), Invoice(1));
        var options = new SubscriptionOptions { CheckpointInterval = TimeSpan.FromMilliseconds(1) };

        await using var subscription = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start, options)
            .GetAsyncEnumerator(ct);

        var messages = await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        await AppendAsync("s1", ct, Invoice(2), Invoice(3), Order(2));
        messages.AddRange(await ReadUntilAsync(subscription, x => TotalOf(x) == 2));

        messages.Count(x => x.IsCheckpoint).ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenLiveAndPassingOverEvents_ReportsHowFarInOrder(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await using var subscription = EventStore.SubscribeToAll(options: OrdersOften).GetAsyncEnumerator(ct);
        await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        // Invoices are passed over.
        await AppendAsync("invoices", ct, Invoice(1), Invoice(2));
        var first = await ReadUntilAsync(subscription, x => x.IsCheckpoint);

        await AppendAsync("orders", ct, Order(1));
        await AppendAsync("invoices", ct, Invoice(3));
        var rest = await ReadUntilAsync(subscription, x => x.Event is not null);
        var last = await ReadUntilAsync(subscription, x => x.IsCheckpoint);

        // Every message is further on in the log than the one before, so a checkpoint never overtakes an event.
        var positions = first.Concat(rest).Concat(last).Select(PositionOf).ToArray();
        positions.ShouldBe(positions.Order().Distinct());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenTheLogGoesQuietAfterEventsItPassedOver_ReportsTheLastOfThem(
        CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        // Long enough for all the invoices to arrive within one interval of each other.
        var options = OrdersOften with { CheckpointInterval = TimeSpan.FromMilliseconds(250) };

        await using var subscription = EventStore.SubscribeToAll(options: options).GetAsyncEnumerator(ct);
        await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        await AppendAsync("invoices", ct, [.. Enumerable.Range(0, 20).Select(Invoice)]);

        var lastInTheLog = await EventStore
            .ReadAll(ReadDirection.Backward, options: new() { MaxCount = 1 })
            .Select(x => x.Context.LogPosition)
            .SingleAsync(ct);

        // Nothing more arrives to prompt it, and a subscriber that resumed would otherwise look at them all again.
        var messages = await ReadUntilAsync(subscription, x => x.LogCheckpoint.Value.Equals(lastInTheLog));

        messages.ShouldAllBe(x => x.IsCheckpoint);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenResumedAfterACheckpoint_MissesNothingAndRepeatsNothing(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("s1", ct, Order(1), Invoice(1), Order(2), Invoice(2), Invoice(3));

        var caughtUp = await ReadUntilCaughtUpAsync(OrdersFromStart(), ct);
        var checkpoint = caughtUp.Single(x => x.IsCheckpoint).LogCheckpoint.Value;

        await AppendAsync("s1", ct, Invoice(4), Order(3));

        var origin = SubscriptionOrigin.After(checkpoint);
        var resumed = await ReadUntilCaughtUpAsync(EventStore.SubscribeToAll(origin, OrdersOften), ct);

        caughtUp.Where(x => x.Event is not null).Select(TotalOf).ShouldBe([1, 2]);
        resumed.Where(x => x.Event is not null).Select(TotalOf).ShouldBe([3]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WhenCatchUpLooksPastTheLastEventItDelivers_ReportsHowFarInTheStream(
        CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("other", ct, Invoice(9), Invoice(9));
        await AppendAsync("s1", ct, Order(1), Invoice(1), Invoice(2));
        await AppendAsync("other", ct, Invoice(9));

        var messages = await ReadUntilCaughtUpAsync(
            EventStore.SubscribeToStream("s1", SubscriptionOrigin.Start, OrdersOften),
            ct);

        // In the position of the stream, which is what a subscription to it resumes after, whatever the log holds.
        messages.Count.ShouldBe(3);
        messages[1].StreamCheckpoint.Value.ShouldBe(CreateStreamPosition(2));
        messages[1].LogCheckpoint.HasValue.ShouldBeFalse();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WhenResumedAfterACheckpoint_MissesNothingAndRepeatsNothing(CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await AppendAsync("s1", ct, Order(1), Invoice(1), Invoice(2));

        var caughtUp = await ReadUntilCaughtUpAsync(
            EventStore.SubscribeToStream("s1", SubscriptionOrigin.Start, OrdersOften),
            ct);

        var checkpoint = caughtUp.Single(x => x.IsCheckpoint).StreamCheckpoint.Value;

        await AppendAsync("other", ct, Order(9));
        await AppendAsync("s1", ct, Invoice(3), Order(2));

        var resumed = await ReadUntilCaughtUpAsync(
            EventStore.SubscribeToStream("s1", SubscriptionOrigin.After(checkpoint), OrdersOften),
            ct);

        resumed.Where(x => x.Event is not null).Select(TotalOf).ShouldBe([2]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WhenLiveAndPassingOverEventsOfTheStream_ReportsHowFarInTheStream(
        CancellationToken ct)
    {
        RequireCapability(StoreCapabilities.SubscriptionCheckpoints);

        await using var subscription = EventStore
            .SubscribeToStream("invoices", options: OrdersOften)
            .GetAsyncEnumerator(ct);

        await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        await AppendAsync("invoices", ct, Invoice(1), Invoice(2));

        // In the position of the stream, and as far as the last of them, though nothing more arrives to prompt it.
        var last = CreateStreamPosition(1);
        var messages = await ReadUntilAsync(subscription, x => x.StreamCheckpoint.Value.Equals(last));

        messages.ShouldAllBe(x => x.IsCheckpoint);
        messages[^1].LogCheckpoint.HasValue.ShouldBeFalse();
    }

    private IAsyncEnumerable<SubscriptionMessage<object, string, TStreamPos, TLogPos>> OrdersFromStart() =>
        EventStore.SubscribeToAll(SubscriptionOrigin.Start, OrdersOften);

    private static OrderPlaced Order(int total) => new() { Total = total };

    // An order is known by its total: it has an array, which a record compares by reference.
    private static int? TotalOf(SubscriptionMessage<object, string, TStreamPos, TLogPos> message) =>
        (message.Event?.Payload as OrderPlaced)?.Total;

    private static InvoiceRaised Invoice(int amount) => new() { Amount = amount };

    private Task AppendAsync(string streamId, CancellationToken ct, params object[] events) =>
        EventStore.AppendAsync(streamId, events, cancellationToken: ct);

    private static async Task<List<SubscriptionMessage<object, string, TStreamPos, TLogPos>>> ReadUntilCaughtUpAsync(
        IAsyncEnumerable<SubscriptionMessage<object, string, TStreamPos, TLogPos>> messages,
        CancellationToken ct)
    {
        await using var subscription = messages.GetAsyncEnumerator(ct);

        return await ReadUntilAsync(subscription, x => x.IsCaughtUp);
    }

    private static async Task<List<SubscriptionMessage<object, string, TStreamPos, TLogPos>>> ReadUntilAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> subscription,
        Func<SubscriptionMessage<object, string, TStreamPos, TLogPos>, bool> isLast)
    {
        var messages = new List<SubscriptionMessage<object, string, TStreamPos, TLogPos>>();

        while (true)
        {
            (await subscription.MoveNextAsync()).ShouldBeTrue();
            subscription.Current.IsFellBehind.ShouldBeFalse();
            messages.Add(subscription.Current);

            if (isLast(subscription.Current))
                return messages;
        }
    }

    private static ulong PositionOf(SubscriptionMessage<object, string, TStreamPos, TLogPos> message) =>
        (message.Event?.Context.LogPosition ?? message.LogCheckpoint.Value).Value;
}