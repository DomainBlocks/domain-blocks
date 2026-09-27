using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// A filter selects the same events however a store evaluates it: in its database as far as it can, or all of it as
/// the events are read. Both are compared with what each filter should select, said by hand.
/// </summary>
public abstract class EventStoreFilterTests<TStreamPos, TLogPos>(IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

    private IReadOnlyList<LoggedEvent> Log { get; set; } = null!;

    public static IEnumerable<EventFilterCase> Cases => EventFilterTestLog.Cases;

    /// <summary>
    /// The format that events are stored in, or <see langword="null"/> for the default of the harness.
    /// </summary>
    protected virtual EventFormat? Format => null;

    [OneTimeSetUp]
    public async Task AppendLogAsync()
    {
        EventStore = CreateEventStore(
            EventFilterTestLog.TypeMap,
            Format,
            ignoredEventNames: [EventFilterTestLog.IgnoredEventName]);

        foreach (var (streamId, e) in EventFilterTestLog.Events())
            await EventStore.AppendAsync(streamId, [e]);

        // An unfiltered read is what filtered reads are measured against. It is made by a store that ignores nothing,
        // as a filter goes by the metadata that is stored, which a store leaves out of the events it ignores.
        await using var everythingStore = CreateEventStore(
            EventFilterTestLog.TypeMap,
            Format,
            loggerNameSuffix: "_everything");

        Log = [.. (await everythingStore.ReadAll().ToArrayAsync()).Select(ToLoggedEvent)];
        Log.Count.ShouldBe(40);

        // A filter that selects nothing, or everything, would pass whatever a store did with it.
        string[] selectNothing = ["None", "EventName_NotInTheLog", "Metadata_NoSuchKey", "OfType_NotMapped"];
        string[] selectEverything = ["All", "OfType_Everything"];

        foreach (var filterCase in Cases)
        {
            var count = Log.Count(x => filterCase.Expected(Log, x));

            if (!selectNothing.Contains(filterCase.Name))
                count.ShouldBeGreaterThan(0, filterCase.Name);

            if (!selectEverything.Contains(filterCase.Name))
                count.ShouldBeLessThan(Log.Count, filterCase.Name);
        }
    }

    [OneTimeTearDown]
    public async Task DisposeStoreAsync()
    {
        if (EventStore is { } eventStore)
            await eventStore.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public void Reads_WhenTheStoreDoesNotFilter_RefuseAFilter()
    {
        if (Harness.Capabilities.HasFlag(StoreCapabilities.EventFilters))
            Assert.Ignore("The store filters.");

        var filter = EventFilter.StreamId("order-1");

        Should.Throw<EventFilterNotSupportedException>(() => EventStore.ReadAll(options: new() { Filter = filter }));

        Should.Throw<EventFilterNotSupportedException>(
            () => EventStore.ReadStream("order-1", options: new() { Filter = filter }));
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithAFilter_SelectsTheSameEventsHoweverItIsEvaluated(
        EventFilterCase filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var filter = filterCase.Filter(Log);
        var expected = Log.Where(x => filterCase.Expected(Log, x)).ToArray();

        foreach (var direction in (ReadDirection[])[ReadDirection.Forward, ReadDirection.Backward])
        {
            var expectedInOrder = direction == ReadDirection.Forward ? expected : [.. expected.Reverse()];

            foreach (var pushdown in (FilterPushdownMode[])[FilterPushdownMode.Prefer, FilterPushdownMode.None])
            {
                var options = new ReadAllOptions { Filter = filter, FilterPushdownMode = pushdown };
                var read = await EventStore.ReadAll(direction, options: options).ToArrayAsync(cancellationToken);

                ShouldBe(read, expectedInOrder, $"{direction}, {pushdown}, {filter}");
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithAFilter_SelectsTheSameEventsHoweverItIsEvaluated(
        EventFilterCase filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        const string streamId = "order-10";
        var filter = filterCase.Filter(Log);
        var expected = Log.Where(x => x.StreamId == streamId && filterCase.Expected(Log, x)).ToArray();

        foreach (var direction in (ReadDirection[])[ReadDirection.Forward, ReadDirection.Backward])
        {
            var expectedInOrder = direction == ReadDirection.Forward ? expected : [.. expected.Reverse()];

            foreach (var pushdown in (FilterPushdownMode[])[FilterPushdownMode.Prefer, FilterPushdownMode.None])
            {
                var options = new ReadStreamOptions { Filter = filter, FilterPushdownMode = pushdown };

                var read = await EventStore
                    .ReadStream(streamId, direction, options: options)
                    .ToArrayAsync(cancellationToken);

                ShouldBe(read, expectedInOrder, $"{direction}, {pushdown}, {filter}");
            }
        }
    }

    [TestCase("Metadata", FilterPushdownMode.Prefer)]
    [TestCase("Metadata", FilterPushdownMode.None)]
    [TestCase("Predicate", FilterPushdownMode.Prefer)]
    [TestCase("Predicate", FilterPushdownMode.None)]
    [TestCase("Or_PredicateAndMetadata", FilterPushdownMode.Prefer)]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithAFilterAndMaxCount_CountsTheEventsSelected(
        string caseName,
        FilterPushdownMode pushdownMode,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var filterCase = Cases.Single(x => x.Name == caseName);
        var expected = Log.Where(x => filterCase.Expected(Log, x)).ToArray();
        expected.Length.ShouldBeGreaterThan(3);

        var options = new ReadAllOptions { Filter = filterCase.Filter(Log), FilterPushdownMode = pushdownMode, MaxCount = 3 };

        var forward = await EventStore.ReadAll(options: options).ToArrayAsync(cancellationToken);
        var backward = await EventStore
            .ReadAll(ReadDirection.Backward, options: options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(forward, expected.Take(3));
        ShouldBe(backward, expected.Reverse().Take(3));
    }

    [TestCase(FilterPushdownMode.Prefer)]
    [TestCase(FilterPushdownMode.None)]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithAMetadataFilterAndMetadataLeftOut_SelectsByItAndLeavesItOut(
        FilterPushdownMode pushdownMode,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var options = new ReadAllOptions
        {
            Filter = !EventFilter.Metadata("tenant", "acme") & EventFilter.MetadataExists("tenant"),
            FilterPushdownMode = pushdownMode,
            IncludeMetadata = false
        };

        var read = await EventStore.ReadAll(options: options).ToArrayAsync(cancellationToken);

        read.Select(x => Describe(x.Payload))
            .ShouldBe(Log.Where(x => x.Tenant is not (null or "acme")).Select(x => Describe(x.Payload)));
        read.ShouldAllBe(x => x.Context.Metadata.Count == 0);
    }

    [TestCase(FilterPushdownMode.Prefer)]
    [TestCase(FilterPushdownMode.None)]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WhenTheLogHasNamesThatAreNotMapped_PassesOverThemWithoutDecoding(
        FilterPushdownMode pushdownMode,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        await using var narrowStore = CreateEventStore(
            EventTypeMap.Create(EventTypeMapping.ReadWrite<OrderPlaced>()),
            Format,
            loggerNameSuffix: "_narrow");

        // Without a filter the read meets a name that it cannot decode.
        await Should.ThrowAsync<EventNameNotMappedException>(
            async () => await narrowStore.ReadAll().ToArrayAsync(cancellationToken));

        var options = new ReadAllOptions { Filter = EventFilter.OfType<OrderPlaced>(), FilterPushdownMode = pushdownMode };
        var read = await narrowStore.ReadAll(options: options).ToArrayAsync(cancellationToken);

        read.Select(x => Describe(x.Payload))
            .ShouldBe(Log.Select(x => x.Payload).OfType<OrderPlaced>().Select(Describe));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WhenAFilterSelectsNothingOfAStreamThatExists_ReturnsNothing(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var options = new ReadStreamOptions
        {
            Filter = EventFilter.EventName("Nope"),
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        (await EventStore.ReadStream("order-1", options: options).ToArrayAsync(cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WhenTheStreamDoesNotExist_ThrowsWhateverTheFilter(CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var options = new ReadStreamOptions
        {
            Filter = EventFilter.None,
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        await Should.ThrowAsync<StreamNotFoundException>(
            async () => await EventStore.ReadStream("no-such-stream", options: options).ToArrayAsync(cancellationToken));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WhenPushdownIsRequired_EvaluatesNamesStreamsAndTypesInTheDatabase(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var filter = EventFilter.OfType<IOrderEvent>() & !EventFilter.StreamIdStartsWith("order-1");
        var options = new ReadAllOptions { Filter = filter, FilterPushdownMode = FilterPushdownMode.Require };

        var read = await EventStore.ReadAll(options: options).ToArrayAsync(cancellationToken);

        ShouldBe(
            read,
            Log.Where(x => x.Payload is IOrderEvent && !x.StreamId.StartsWith("order-1", StringComparison.Ordinal)));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public void ReadAll_WhenPushdownIsRequiredOfAPredicate_IsRefusedAtTheCall()
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var options = new ReadAllOptions
        {
            Filter = EventFilter.OfType<OrderPlaced>(e => e.Total > 0),
            FilterPushdownMode = FilterPushdownMode.Require
        };

        Should.Throw<EventFilterNotSupportedException>(() => EventStore.ReadAll(options: options));
    }

    private static void ShouldBe(
        IEnumerable<ReadEvent<object, string, TStreamPos, TLogPos>> read,
        IEnumerable<LoggedEvent> expected,
        string? because = null)
    {
        var events = read.ToArray();
        var expectedEvents = expected.ToArray();

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