using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// A subscription with a filter delivers the events that the filter selects, in order, while it catches up and once
/// it is live, and is not troubled by the events that it passes over.
/// </summary>
/// <remarks>
/// The log is not reset between tests, so each test goes by the log as it finds it. A selective subscription gives no
/// sign that it has seen an event it does not select, so a test that waits for live events has its filter select a
/// marker as well, which it appends last.
/// </remarks>
public abstract class EventStoreFilteredSubscriptionTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private const string StreamId = "order-10";

    private static readonly EventTypeMap TypeMap = EventTypeMap.Create(
        EventTypeMapping.ReadWrite<OrderPlaced>(),
        EventTypeMapping.ReadWrite<OrderShipped>(),
        EventTypeMapping.ReadWrite<InvoiceRaised>(),
        EventTypeMapping.ReadWrite<OrderRetired>(),
        EventTypeMapping.ReadWrite<FilterTestMarker>());

    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

    // Reads the log as it is stored, ignoring nothing, for saying what a filter should select.
    private IEventStore<object, string, TStreamPos, TLogPos> EverythingStore { get; set; } = null!;

    public static IEnumerable<EventFilterCase> Cases => EventFilterTestLog.Cases;

    /// <summary>
    /// The format that events are stored in, or <see langword="null"/> for the default of the harness.
    /// </summary>
    protected virtual EventFormat? Format => null;

    public static IEnumerable<TestCaseData> CasesPushedDownAndNot =>
        from filterCase in EventFilterTestLog.Cases
        from pushdown in (FilterPushdownMode[])[FilterPushdownMode.Prefer, FilterPushdownMode.None]
        select new TestCaseData(filterCase, pushdown);

    [OneTimeSetUp]
    public void CreateStores()
    {
        EventStore = CreateEventStore(TypeMap, Format, ignoredEventNames: [EventFilterTestLog.IgnoredEventName]);
        EverythingStore = CreateEventStore(TypeMap, Format, loggerNameSuffix: "_everything");
    }

    /// <summary>
    /// The tests say what they expect of the log as they find it, so it need not be the same for each. But left to
    /// grow by what the live tests append, it was read in full by every test after them, which came to most of the
    /// time the suite took. So it is started again once it is ten times what it was. Not before every test, as
    /// appending it costs far more than reading it to see.
    /// </summary>
    [SetUp]
    public async Task KeepLogShortAsync()
    {
        var count = await EverythingStore.ReadAll().CountAsync();

        if (count > 0 && count <= 10 * EventFilterTestLog.Events().Count())
            return;

        await Harness.ResetAsync();
        await AppendLogAsync(EventStore);
    }

    [OneTimeTearDown]
    public async Task DisposeStoresAsync()
    {
        if (EventStore is { } eventStore)
            await eventStore.DisposeAsync();

        if (EverythingStore is { } everythingStore)
            await everythingStore.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public void Subscriptions_WhenTheStoreDoesNotFilterThem_RefuseAFilter()
    {
        if (Harness.Capabilities.HasFlag(StoreCapabilities.EventFilteredSubscriptions))
            Assert.Ignore("The store filters subscriptions.");

        var options = new SubscriptionOptions { Filter = EventFilter.StreamId(StreamId) };

        Should.Throw<EventFilterNotSupportedException>(() => EventStore.SubscribeToAll(options: options));
        Should.Throw<EventFilterNotSupportedException>(() => EventStore.SubscribeToStream(StreamId, options: options));
    }

    [TestCaseSource(nameof(CasesPushedDownAndNot))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhileCatchingUp_DeliversWhatTheFilterSelects(
        EventFilterCase filterCase,
        FilterPushdownMode pushdownMode,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilteredSubscriptions);

        var log = await ReadLogAsync(cancellationToken);
        var options = new SubscriptionOptions { Filter = filterCase.Filter(log), FilterPushdownMode = pushdownMode };

        await using var subscription = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start, options)
            .GetAsyncEnumerator(cancellationToken);

        var delivered = await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        ShouldBe(delivered, log.Where(x => filterCase.Expected(log, x)), options.Filter.ToString());
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenLive_DeliversWhatTheFilterSelects(
        EventFilterCase filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilteredSubscriptions);

        var logBefore = await ReadLogAsync(cancellationToken);
        var marker = new FilterTestMarker { Id = $"{Guid.NewGuid():N}" };
        var filter = filterCase.Filter(logBefore) | EventFilter.EventName(nameof(FilterTestMarker));

        await using var subscription = EventStore
            .SubscribeToAll(options: new SubscriptionOptions { Filter = filter })
            .GetAsyncEnumerator(cancellationToken);

        (await ReadUntilAsync(subscription, x => x.IsCaughtUp)).ShouldBeEmpty();

        await AppendLogAsync(EventStore);
        await EventStore.AppendAsync($"marker-{marker.Id}", [marker], cancellationToken: cancellationToken);

        var delivered = await ReadUntilAsync(subscription, x => x.Event?.Payload.Equals(marker) == true);
        var log = await ReadLogAsync(cancellationToken);

        // The filter was made from the log as it was, so what it should select is said of that log too.
        var expected = log.Skip(logBefore.Count).Where(x => filterCase.Expected(logBefore, x));

        ShouldBe(delivered, expected, filter.ToString());
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WhileCatchingUpAndWhenLive_DeliversWhatTheFilterSelects(
        EventFilterCase filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilteredSubscriptions);

        var logBefore = await ReadLogAsync(cancellationToken);
        var marker = new FilterTestMarker { Id = $"{Guid.NewGuid():N}" };
        var filter = filterCase.Filter(logBefore) | EventFilter.EventName(nameof(FilterTestMarker));

        await using var subscription = EventStore
            .SubscribeToStream(StreamId, SubscriptionOrigin.Start, new SubscriptionOptions { Filter = filter })
            .GetAsyncEnumerator(cancellationToken);

        var caughtUp = await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        await AppendLogAsync(EventStore);
        await EventStore.AppendAsync(StreamId, [marker], cancellationToken: cancellationToken);

        var live = await ReadUntilAsync(subscription, x => x.Event?.Payload.Equals(marker) == true);
        var log = await ReadLogAsync(cancellationToken);

        var expected = log.Where(x => x.StreamId == StreamId && filterCase.Expected(logBefore, x));

        ShouldBe([.. caughtUp, .. live], expected, filter.ToString());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenItSelectsLittle_IsNotOverrunByWhatItPassesOver(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilteredSubscriptions);

        var marker = new FilterTestMarker { Id = $"{Guid.NewGuid():N}" };
        var options = new SubscriptionOptions
        {
            Filter = EventFilter.EventName(nameof(FilterTestMarker)),
            QueueCapacity = 1
        };

        await using var subscription = EventStore.SubscribeToAll(options: options).GetAsyncEnumerator(cancellationToken);
        await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        // Far more than the queue holds, none of which the filter selects, so none of which is queued.
        var passedOver = Enumerable.Range(0, 200).Select(i => new InvoiceRaised { Amount = i });
        await EventStore.AppendAsync("invoice-0", passedOver, cancellationToken: cancellationToken);
        await EventStore.AppendAsync($"marker-{marker.Id}", [marker], cancellationToken: cancellationToken);

        // It may say how far it has looked first, which takes no place in the queue that an event could have had.
        var events = await ReadUntilAsync(subscription, x => x.Event is not null);

        events.ShouldHaveSingleItem().Payload.ShouldBe(marker);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenItFallsBehindWithAFilter_RecoversWithoutLossOrRepeats(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilteredSubscriptions);

        var streamId = $"overflow-{Guid.NewGuid():N}";
        var options = new SubscriptionOptions
        {
            Filter = EventFilter.StreamId(streamId) & EventFilter.EventName(nameof(OrderPlaced)),
            QueueCapacity = 1
        };

        await using var subscription = EventStore.SubscribeToAll(options: options).GetAsyncEnumerator(cancellationToken);
        await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        await using var witness = EventStore
            .SubscribeToAll(options: options with { QueueCapacity = 1000 })
            .GetAsyncEnumerator(cancellationToken);

        await ReadUntilAsync(witness, x => x.IsCaughtUp);

        // Far more orders than the queue holds, with an invoice to pass over between each.
        var appended = Enumerable.Range(0, 100)
            .Select(object (i) => i % 2 == 0 ? new OrderPlaced { Total = i } : new InvoiceRaised { Amount = i });

        await EventStore.AppendAsync(streamId, appended, cancellationToken: cancellationToken);

        // The two share the live feed of the store. Once the witness has the last order, the feed has offered every
        // one of them to the subscription too, which nobody was reading, so its queue has been overrun.
        await ReadUntilAsync(witness, x => x.Event?.Payload is OrderPlaced { Total: 98 });

        var totals = new List<int>();
        var fellBehindCount = 0;
        var caughtUpCount = 0;

        while (fellBehindCount == 0 || caughtUpCount < fellBehindCount)
        {
            // A subscription that was not overrun after all would wait here for ever, with nothing to say why.
            var next = subscription.MoveNextAsync().AsTask();

            if (await Task.WhenAny(next, Task.Delay(10000, cancellationToken)) != next)
            {
                Assert.Fail(
                    $"Nothing more after {totals.Count} orders, having fallen behind {fellBehindCount} times and " +
                    $"caught up {caughtUpCount} times.");
            }

            (await next).ShouldBeTrue();

            switch (subscription.Current)
            {
                case { Event: { } e }:
                    totals.Add(e.Payload.ShouldBeOfType<OrderPlaced>().Total);
                    break;
                case { IsCaughtUp: true }:
                    caughtUpCount++;
                    break;
                case { IsFellBehind: true }:
                    fellBehindCount++;
                    break;
            }
        }

        // It starts again from where it had got to, which with a filter may be a checkpoint and not an event.
        totals.ShouldBe(Enumerable.Range(0, 50).Select(x => x * 2));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenACheaperFilterRulesAnEventOut_DoesNotEvaluateThePredicate(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilteredSubscriptions);

        var marker = new FilterTestMarker { Id = $"{Guid.NewGuid():N}" };

        // The predicate is written first, and fails the subscription if it is given an order from another stream.
        var filter = (EventFilter.OfType<OrderPlaced>(e => IsFromTheStream(e)) & EventFilter.StreamId("only-here")) |
                     EventFilter.EventName(nameof(FilterTestMarker));

        await using var subscription = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start, new SubscriptionOptions { Filter = filter })
            .GetAsyncEnumerator(cancellationToken);

        await ReadUntilAsync(subscription, x => x.IsCaughtUp);

        await EventStore.AppendAsync("order-1", [new OrderPlaced { Total = -1 }], cancellationToken: cancellationToken);
        await EventStore.AppendAsync("only-here", [new OrderPlaced { Total = 1 }], cancellationToken: cancellationToken);
        await EventStore.AppendAsync($"marker-{marker.Id}", [marker], cancellationToken: cancellationToken);

        var delivered = await ReadUntilAsync(subscription, x => x.Event?.Payload.Equals(marker) == true);

        delivered.Select(x => x.Payload).OfType<OrderPlaced>().Select(x => x.Total).ShouldBe([1]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenAnEventItPassesOverCannotBeDecoded_CarriesOn(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilteredSubscriptions);

        // Reads orders alone, so every other name in the log is one it cannot decode.
        await using var narrowStore = CreateEventStore(
            EventTypeMap.Create(EventTypeMapping.ReadWrite<OrderPlaced>()),
            Format,
            loggerNameSuffix: "_narrow");

        var options = new SubscriptionOptions { Filter = EventFilter.OfType<OrderPlaced>() };

        await using var selective = narrowStore.SubscribeToAll(options: options).GetAsyncEnumerator(cancellationToken);
        await using var unfiltered = narrowStore.SubscribeToAll().GetAsyncEnumerator(cancellationToken);

        await ReadUntilAsync(selective, x => x.IsCaughtUp);
        await ReadUntilAsync(unfiltered, x => x.IsCaughtUp);

        var order = new OrderPlaced { Total = 7 };
        var invoice = new InvoiceRaised { Amount = 1 };
        await EventStore.AppendAsync("invoice-0", [invoice], cancellationToken: cancellationToken);
        await EventStore.AppendAsync("order-1", [order], cancellationToken: cancellationToken);

        // The subscription that has to decode the invoice fails, and the one that passes over it does not.
        await Should.ThrowAsync<EventNameNotMappedException>(async () => await unfiltered.MoveNextAsync());

        (await selective.MoveNextAsync()).ShouldBeTrue();
        Describe(selective.Current.Event.ShouldNotBeNull().Payload).ShouldBe(Describe(order));
    }

    private static bool IsFromTheStream(OrderPlaced e) =>
        e.Total > 0 ? true : throw new InvalidOperationException("The predicate was given an order it should not see.");

    private static async Task AppendLogAsync(IEventStore<object, string, TStreamPos, TLogPos> eventStore)
    {
        foreach (var (streamId, e) in EventFilterTestLog.Events())
            await eventStore.AppendAsync(streamId, [e]);
    }

    private async Task<IReadOnlyList<LoggedEvent>> ReadLogAsync(CancellationToken cancellationToken)
    {
        var events = await EverythingStore.ReadAll().ToArrayAsync(cancellationToken);

        return [.. events.Select(ToLoggedEvent)];
    }

    private static async Task<List<ReadEvent<object, string, TStreamPos, TLogPos>>> ReadUntilAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> subscription,
        Func<SubscriptionMessage<object, string, TStreamPos, TLogPos>, bool> isLast)
    {
        var events = new List<ReadEvent<object, string, TStreamPos, TLogPos>>();

        while (true)
        {
            (await subscription.MoveNextAsync()).ShouldBeTrue();

            var message = subscription.Current;
            message.IsFellBehind.ShouldBeFalse();

            if (message.Event is { } e)
                events.Add(e);

            if (isLast(message))
                return events;
        }
    }

    // Markers are how a test knows where to stop, and no part of what a filter is said to select.
    private static void ShouldBe(
        IEnumerable<ReadEvent<object, string, TStreamPos, TLogPos>> delivered,
        IEnumerable<LoggedEvent> expected,
        string because)
    {
        var events = delivered.Where(x => x.Payload is not FilterTestMarker).ToArray();
        var expectedEvents = expected.Where(x => x.Payload is not FilterTestMarker).ToArray();

        events.Select(x => (object)x.Context.LogPosition).ShouldBe(expectedEvents.Select(x => x.LogPosition), because);
        events.Select(x => Describe(x.Payload))
            .ShouldBe(expectedEvents.Select(x => Describe(x.Payload)), because);
    }

    private static string Describe(object payload) => EventFilterTestLog.Describe(payload);

    private LoggedEvent ToLoggedEvent(ReadEvent<object, string, TStreamPos, TLogPos> e)
    {
        var context = e.Context;
        var payload = context.EventName == EventFilterTestLog.IgnoredEventName ? IgnoredEvent.Instance : e.Payload;

        return new LoggedEvent(
            context.LogPosition,
            payload,
            context.EventName,
            context.StreamId,
            context.Metadata,
            context.CreatedAt);
    }
}