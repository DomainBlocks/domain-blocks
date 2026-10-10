using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// Subscribing with an event filter. Each case of <see cref="EventFilterTestLog"/> is subscribed to from the start of
/// the log, which a store catches up on by reading, and from its end, where the events of the test log are appended
/// again and observed live. Either way, what a subscription observes is compared with what the case expects of the same
/// events as they are read without a filter. The log grows as the tests run, so each test reads it when it needs it. A
/// store that does not filter subscriptions refuses a filter rather than ignoring it.
/// </summary>
public abstract class EventStoreFilteredSubscriptionTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    // This stream of the test log has events of every shape of metadata.
    private const string StreamId = "order-1";

    private const string Placed = nameof(EventFilterTestLog.OrderPlaced);
    private const string Shipped = nameof(EventFilterTestLog.OrderShipped);
    private const string BatchEnded = nameof(EventFilterTestLog.BatchEnded);

    private IEventStore<object, string, TStreamPos, TLogPos> _eventStore = null!;
    private DateTimeOffset _midpoint;

    public static IEnumerable<EventFilterTestLog.Case> Cases => EventFilterTestLog.Cases;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _eventStore = CreateEventStore(EventFilterTestLog.TypeMap);

        var index = 0;

        foreach (var (streamId, e) in EventFilterTestLog.GenerateEvents())
        {
            if (EventFilterTestLog.PauseBeforeIndexes.Contains(index++))
                await Task.Delay(50);

            await _eventStore.AppendAsync(streamId, [e]);
        }

        var log = await ReadLogAsync(CancellationToken.None);
        _midpoint = log[EventFilterTestLog.PauseBeforeIndexes[0]].Logged.CreatedAt;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_eventStore is { } eventStore)
            await eventStore.DisposeAsync();
    }

    [Test]
    public void SubscribeToAll_WithFilterWhenStoreDoesNotFilterSubscriptions_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(Placed) };

        Should.Throw<EventFilterNotSupportedException>(() => _eventStore.SubscribeToAll(options: options));
    }

    [Test]
    public void SubscribeToStream_WithFilterWhenStoreDoesNotFilterSubscriptions_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(Placed) };

        Should.Throw<EventFilterNotSupportedException>(() => _eventStore.SubscribeToStream(StreamId, options: options));
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithFilterFromStart_CatchesUpOnSelectedEvents(
        EventFilterTestLog.Case filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = filterCase.Filter(_midpoint) };

        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start, options)
            .GetAsyncEnumerator(cancellationToken);

        var observed = await ReadUntilCaughtUpAsync(enumerator);

        var log = await ReadLogAsync(cancellationToken);
        ShouldBe(observed, log.Where(e => filterCase.Expected(_midpoint, e.Logged)));
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WithFilterFromStart_CatchesUpOnSelectedEventsOfStream(
        EventFilterTestLog.Case filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = filterCase.Filter(_midpoint) };

        await using var enumerator = _eventStore
            .SubscribeToStream(StreamId, SubscriptionOrigin.Start, options)
            .GetAsyncEnumerator(cancellationToken);

        var observed = await ReadUntilCaughtUpAsync(enumerator);

        var log = await ReadLogAsync(cancellationToken);
        ShouldBe(observed, log.Where(e => e.Logged.StreamId == StreamId && filterCase.Expected(_midpoint, e.Logged)));
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithFilter_ObservesSelectedLiveEvents(
        EventFilterTestLog.Case filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions
        {
            Filter = filterCase.Filter(_midpoint) | EventFilter.EventNames(BatchEnded)
        };

        await using var enumerator = _eventStore
            .SubscribeToAll(options: options)
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        var batch = await AppendBatchAsync(cancellationToken);
        var observed = await ReadUntilBatchEndedAsync(enumerator);

        ShouldBe(observed, batch.Where(e => filterCase.Expected(_midpoint, e.Logged)));
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WithFilter_ObservesSelectedLiveEventsOfStream(
        EventFilterTestLog.Case filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions
        {
            Filter = filterCase.Filter(_midpoint) | EventFilter.EventNames(BatchEnded)
        };

        await using var enumerator = _eventStore
            .SubscribeToStream(StreamId, options: options)
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        var batch = await AppendBatchAsync(cancellationToken);
        var observed = await ReadUntilBatchEndedAsync(enumerator);

        ShouldBe(
            observed,
            batch.Where(e => e.Logged.StreamId == StreamId && filterCase.Expected(_midpoint, e.Logged)));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithFilterAfterPosition_CatchesUpOnSelectedEventsAfterIt(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        const int afterIndex = 17;
        var log = await ReadLogAsync(cancellationToken);
        var origin = SubscriptionOrigin.After(log[afterIndex].Read.Context.LogPosition);
        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(Shipped) };

        await using var enumerator = _eventStore
            .SubscribeToAll(origin, options)
            .GetAsyncEnumerator(cancellationToken);

        var observed = await ReadUntilCaughtUpAsync(enumerator);

        ShouldBe(observed, log.Where(e => e.Logged.Index > afterIndex && e.Logged.EventName == Shipped));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WithFilterAfterPosition_CatchesUpOnSelectedEventsOfStreamAfterIt(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var log = await ReadLogAsync(cancellationToken);
        LogEntry[] streamEvents = [.. log.Where(e => e.Logged.StreamId == StreamId)];
        var after = streamEvents[3];
        var origin = SubscriptionOrigin.After(after.Read.Context.StreamPosition);
        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(Shipped) };

        await using var enumerator = _eventStore
            .SubscribeToStream(StreamId, origin, options)
            .GetAsyncEnumerator(cancellationToken);

        var observed = await ReadUntilCaughtUpAsync(enumerator);

        ShouldBe(
            observed,
            streamEvents.Where(e => e.Logged.Index > after.Logged.Index && e.Logged.EventName == Shipped));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithFilterFromStartAndThenLiveEvents_ObservesEachSelectedEventOnce(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions
        {
            Filter = EventFilter.Metadata("tenant", "acme") | EventFilter.EventNames(BatchEnded)
        };

        await using var enumerator = _eventStore
            .SubscribeToAll(SubscriptionOrigin.Start, options)
            .GetAsyncEnumerator(cancellationToken);

        var caughtUpOn = await ReadUntilCaughtUpAsync(enumerator);
        var logBefore = await ReadLogAsync(cancellationToken);

        var batch = await AppendBatchAsync(cancellationToken);
        var live = await ReadUntilBatchEndedAsync(enumerator);

        ShouldBe(caughtUpOn, logBefore.Where(e => e.Logged.Tenant == "acme" || e.Logged.EventName == BatchEnded));
        ShouldBe(live, batch.Where(e => e.Logged.Tenant == "acme"));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithCreatedAtFilter_ObservesLiveEventsFromBoundOnwards(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        // The bound is a second after an event that the store has just appended, by the store's own clock.
        var streamId = NewStreamId();
        await AppendAsync(streamId, new EventFilterTestLog.OrderPlaced { Number = 0 }, cancellationToken);
        var logBefore = await ReadLogAsync(cancellationToken);
        var bound = logBefore[^1].Logged.CreatedAt.AddSeconds(1);
        var options = new SubscriptionOptions { Filter = EventFilter.CreatedAtOrAfter(bound) };

        await using var enumerator = _eventStore
            .SubscribeToAll(options: options)
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        // Events are appended until the subscription observes one, which it does not until the bound has passed.
        var next = GetNextEventAsync(enumerator);

        for (var number = 1; !next.IsCompleted; number++)
        {
            await AppendAsync(streamId, new EventFilterTestLog.OrderPlaced { Number = number }, cancellationToken);
            await Task.Delay(100, cancellationToken);
        }

        var observed = await next;

        var appended = (await ReadLogAsync(cancellationToken))[logBefore.Length..];
        appended.ShouldContain(e => e.Logged.CreatedAt < bound);
        ShouldBe([observed], [appended.First(e => e.Logged.CreatedAt >= bound)]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenEventsThatFilterExcludesExceedQueue_DoesNotFallBehind(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(BatchEnded), QueueCapacity = 1 };

        await using var enumerator = _eventStore
            .SubscribeToAll(options: options)
            .GetAsyncEnumerator(cancellationToken);

        // A second subscription shows when the live feed has delivered all the appended events.
        await using var witness = _eventStore.SubscribeToAll().GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);
        await ShouldBeCaughtUpAsync(witness);

        // The subscription is not being read while far more events are appended than its queue holds. It selects only
        // the last of them, so the rest never reach its queue.
        await AppendBatchAsync(cancellationToken);
        await ReadUntilBatchEndedAsync(witness);

        (await GetNextEventAsync(enumerator)).Context.EventName.ShouldBe(BatchEnded);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithFilterWhenQueueOverflows_ReportsFellBehindAndRecoversSelectedEvents(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(Placed), QueueCapacity = 1 };

        await using var enumerator = _eventStore
            .SubscribeToAll(options: options)
            .GetAsyncEnumerator(cancellationToken);

        // A second subscription shows when the live feed has delivered all the appended events.
        await using var witness = _eventStore.SubscribeToAll().GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);
        await ShouldBeCaughtUpAsync(witness);

        // The subscription is not being read while more events that it selects are appended than its queue holds.
        var batch = await AppendBatchAsync(cancellationToken);
        await ReadUntilBatchEndedAsync(witness);

        var observed = new List<ReadEvent<object, string, TStreamPos, TLogPos>>();

        while (true)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();

            if (enumerator.Current.IsFellBehind)
                break;

            observed.Add(enumerator.Current.Event.ShouldNotBeNull());
        }

        // It catches up on the rest by reading, with the same filter.
        observed.AddRange(await ReadUntilCaughtUpAsync(enumerator));

        ShouldBe(observed, batch.Where(e => e.Logged.EventName == Placed));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithFilterExcludingEventsThatCannotBeDecoded_ObservesSelectedEvents(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredSubscriptions);

        // This store maps only two of the kinds of event in the log.
        var eventTypeMap = new EventTypeMapBuilder()
            .Add<EventFilterTestLog.OrderPlaced>()
            .Add<EventFilterTestLog.OrderShipped>()
            .Build();

        await using var eventStore = CreateEventStore(eventTypeMap, loggerNameSuffix: "_narrow");
        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(Placed, Shipped) };

        await using var enumerator = eventStore
            .SubscribeToAll(SubscriptionOrigin.Start, options)
            .GetAsyncEnumerator(cancellationToken);

        var caughtUpOn = await ReadUntilCaughtUpAsync(enumerator);

        // Live, an event that the store cannot decode is followed by one that it can.
        var streamId = NewStreamId();
        await AppendAsync(streamId, new EventFilterTestLog.InvoiceRaised { Number = 0 }, cancellationToken);
        await AppendAsync(streamId, new EventFilterTestLog.OrderPlaced { Number = 1 }, cancellationToken);
        var live = await GetNextEventAsync(enumerator);

        var log = await ReadLogAsync(cancellationToken);
        ShouldBe([.. caughtUpOn, live], log.Where(e => e.Logged.EventName is Placed or Shipped));

        // Without the filter, the store fails on the first event that it cannot decode.
        await using var unfiltered = eventStore
            .SubscribeToAll(SubscriptionOrigin.Start)
            .GetAsyncEnumerator(cancellationToken);

        await Should.ThrowAsync<EventNameNotMappedException>(() => ReadUntilCaughtUpAsync(unfiltered));
    }

    private static string NewStreamId() => $"test-{Guid.NewGuid():N}";

    private Task AppendAsync(string streamId, object payload, CancellationToken cancellationToken) =>
        _eventStore.AppendAsync(streamId, [AppendableEvent.Create(payload)], cancellationToken: cancellationToken);

    // Appends the events of the test log again, a stream at a time, and then an event that marks the end of the batch.
    // Returns the events of the batch, without the one that ends it, as they are read without a filter.
    private async Task<LogEntry[]> AppendBatchAsync(CancellationToken cancellationToken)
    {
        var countBefore = (await ReadLogAsync(cancellationToken)).Length;

        foreach (var stream in EventFilterTestLog.GenerateEvents().GroupBy(x => x.StreamId))
        {
            await _eventStore.AppendAsync(
                stream.Key,
                stream.Select(x => x.Event),
                cancellationToken: cancellationToken);
        }

        await AppendAsync(StreamId, new EventFilterTestLog.BatchEnded(), cancellationToken);

        var log = await ReadLogAsync(cancellationToken);

        return log[countBefore..^1];
    }

    // The log as it is read without a filter, which is what every subscription is compared with.
    private async Task<LogEntry[]> ReadLogAsync(CancellationToken cancellationToken)
    {
        var log = await _eventStore.ReadAll().ToArrayAsync(cancellationToken);

        return
        [
            .. log.Select((x, i) => new LogEntry(
                x,
                new EventFilterTestLog.LoggedEvent(
                    i,
                    x.Context.EventName,
                    x.Context.StreamId,
                    x.Context.Metadata,
                    x.Context.CreatedAt)))
        ];
    }

    private static async Task<ReadEvent<object, string, TStreamPos, TLogPos>> GetNextEventAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        return enumerator.Current.Event.ShouldNotBeNull();
    }

    private static async Task ShouldBeCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }

    private static async Task<List<ReadEvent<object, string, TStreamPos, TLogPos>>> ReadUntilCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> enumerator)
    {
        var events = new List<ReadEvent<object, string, TStreamPos, TLogPos>>();

        while (true)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();

            if (enumerator.Current.IsCaughtUp)
                return events;

            events.Add(enumerator.Current.Event.ShouldNotBeNull());
        }
    }

    // Reads the events of a batch, up to the event that ends it and without that event.
    private static async Task<List<ReadEvent<object, string, TStreamPos, TLogPos>>> ReadUntilBatchEndedAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, TStreamPos, TLogPos>> enumerator)
    {
        var events = new List<ReadEvent<object, string, TStreamPos, TLogPos>>();

        while (true)
        {
            var e = await GetNextEventAsync(enumerator);

            if (e.Context.EventName == BatchEnded)
                return events;

            events.Add(e);
        }
    }

    // Events are told apart by their position in the log. The payload is compared as well, as it is what was decoded.
    private static void ShouldBe(
        IReadOnlyList<ReadEvent<object, string, TStreamPos, TLogPos>> observed,
        IEnumerable<LogEntry> expected)
    {
        LogEntry[] expectedEntries = [.. expected];

        observed.Select(x => x.Context.LogPosition).ShouldBe(expectedEntries.Select(x => x.Read.Context.LogPosition));
        observed.Select(x => x.Payload).ShouldBe(expectedEntries.Select(x => x.Read.Payload));
    }

    // An event as it is read without a filter, together with the form that the cases' expectations are written against.
    private sealed record LogEntry(
        ReadEvent<object, string, TStreamPos, TLogPos> Read,
        EventFilterTestLog.LoggedEvent Logged);
}