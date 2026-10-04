using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// Reading with an event filter. Each case of <see cref="EventFilterTestLog"/> is read from one log, and what a store
/// reads is compared with what the case expects, which is written by hand. The same expectation is checked against
/// the filter evaluated in memory, so the store and the in-memory evaluation are each held to the same meaning.
/// A store that does not filter reads refuses a filter rather than ignoring it.
/// </summary>
public abstract class EventStoreFilteredReadTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    // A stream of the test log with events of every shape of metadata.
    private const string StreamId = "order-1";

    private IEventStore<object, string, TStreamPos, TLogPos> _eventStore = null!;
    private ReadEvent<object, string, TStreamPos, TLogPos>[] _log = [];
    private LoggedEvent[] _loggedEvents = [];
    private DateTimeOffset _midpoint;

    public static IEnumerable<EventFilterCase> Cases => EventFilterTestLog.Cases;

    [OneTimeSetUp]
    public async Task AppendLogAsync()
    {
        _eventStore = CreateEventStore(EventFilterTestLog.TypeMap);

        var index = 0;

        foreach (var (streamId, e) in EventFilterTestLog.Events())
        {
            if (EventFilterTestLog.PauseBeforeIndexes.Contains(index++))
                await Task.Delay(50);

            await _eventStore.AppendAsync(streamId, [e]);
        }

        // The log as it is read without a filter is what every filtered read is compared with.
        _log = await _eventStore.ReadAll().ToArrayAsync();

        _loggedEvents =
        [
            .. _log.Select((x, i) => new LoggedEvent(
                i,
                x.Context.EventName,
                x.Context.StreamId,
                x.Context.Metadata,
                x.Context.CreatedAt))
        ];

        _midpoint = _loggedEvents[EventFilterTestLog.PauseBeforeIndexes[0]].CreatedAt;
    }

    [OneTimeTearDown]
    public async Task DisposeStoreAsync()
    {
        if (_eventStore is { } eventStore)
            await eventStore.DisposeAsync();
    }

    [Test]
    public void ReadAll_WithoutFilter_ReadsLogThatSpansSeveralInstants()
    {
        _loggedEvents.Length.ShouldBe(EventFilterTestLog.Count);

        foreach (var index in EventFilterTestLog.PauseBeforeIndexes)
            _loggedEvents[index - 1].CreatedAt.ShouldBeLessThan(_loggedEvents[index].CreatedAt);
    }

    [TestCaseSource(nameof(Cases))]
    public void Expected_AnyCase_SelectsAsMuchOfLogAsCaseDeclares(EventFilterCase filterCase)
    {
        var count = Expected(filterCase).Length;

        switch (filterCase.Selects)
        {
            case CaseSelection.Nothing:
                count.ShouldBe(0);
                break;

            case CaseSelection.Everything:
                count.ShouldBe(_loggedEvents.Length);
                break;

            default:
                count.ShouldBeInRange(1, _loggedEvents.Length - 1);
                break;
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void Matches_AnyCase_SelectsEventsThatCaseExpects(EventFilterCase filterCase)
    {
        var filter = filterCase.Filter(_midpoint);

        foreach (var e in _loggedEvents)
            filter.Matches(e).ShouldBe(filterCase.Expected(_midpoint, e), $"{filter} on {e}");
    }

    [Test]
    public void ReadAll_WithFilterWhenStoreDoesNotFilterReads_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredReads);

        var options = new ReadAllOptions { Filter = EventFilter.EventNames(nameof(OrderPlaced)) };

        Should.Throw<EventFilterNotSupportedException>(() => _eventStore.ReadAll(options: options));
    }

    [Test]
    public void ReadStream_WithFilterWhenStoreDoesNotFilterReads_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredReads);

        var options = new ReadStreamOptions { Filter = EventFilter.EventNames(nameof(OrderPlaced)) };

        Should.Throw<EventFilterNotSupportedException>(() => _eventStore.ReadStream(StreamId, options: options));
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithFilter_ReadsSelectedEventsInOrder(
        EventFilterCase filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        var options = new ReadAllOptions { Filter = filterCase.Filter(_midpoint) };
        var expected = Expected(filterCase);

        var forward = await _eventStore.ReadAll(options: options).ToArrayAsync(cancellationToken);
        ShouldBe(forward, expected);

        var backward = await _eventStore
            .ReadAll(ReadDirection.Backward, options: options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(backward, expected.Reverse());
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithFilter_ReadsSelectedEventsOfStreamInOrder(
        EventFilterCase filterCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        var options = new ReadStreamOptions { Filter = filterCase.Filter(_midpoint) };
        LoggedEvent[] expected = [.. Expected(filterCase).Where(e => e.StreamId == StreamId)];

        var forward = await _eventStore.ReadStream(StreamId, options: options).ToArrayAsync(cancellationToken);
        ShouldBe(forward, expected);

        var backward = await _eventStore
            .ReadStream(StreamId, ReadDirection.Backward, options: options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(backward, expected.Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithFilterAndMaxCount_CountsSelectedEvents(CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        var options = new ReadAllOptions { Filter = EventFilter.MetadataExists("tenant"), MaxCount = 3 };
        LoggedEvent[] expected = [.. _loggedEvents.Where(e => e.Metadata.ContainsKey("tenant"))];
        expected.Length.ShouldBeGreaterThan(3);

        var forward = await _eventStore.ReadAll(options: options).ToArrayAsync(cancellationToken);
        ShouldBe(forward, expected.Take(3));

        var backward = await _eventStore
            .ReadAll(ReadDirection.Backward, options: options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(backward, expected.Reverse().Take(3));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithFilterAndMaxCount_CountsSelectedEvents(CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        var options = new ReadStreamOptions { Filter = !EventFilter.MetadataExists("note"), MaxCount = 3 };

        LoggedEvent[] expected =
        [
            .. _loggedEvents.Where(e => e.StreamId == StreamId && !e.Metadata.ContainsKey("note"))
        ];

        expected.Length.ShouldBeGreaterThan(3);

        var forward = await _eventStore.ReadStream(StreamId, options: options).ToArrayAsync(cancellationToken);
        ShouldBe(forward, expected.Take(3));

        var backward = await _eventStore
            .ReadStream(StreamId, ReadDirection.Backward, options: options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(backward, expected.Reverse().Take(3));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithFilterFromOrigin_ReadsSelectedEventsFromThere(CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        const int originIndex = 17;
        var origin = ReadOrigin.At(_log[originIndex].Context.LogPosition);
        var options = new ReadAllOptions { Filter = EventFilter.EventNames(nameof(OrderShipped)) };
        LoggedEvent[] expected = [.. _loggedEvents.Where(e => e.EventName == nameof(OrderShipped))];

        var forward = await _eventStore
            .ReadAll(ReadDirection.Forward, origin, options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(forward, expected.Where(e => e.Index >= originIndex));

        var backward = await _eventStore
            .ReadAll(ReadDirection.Backward, origin, options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(backward, expected.Where(e => e.Index <= originIndex).Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithFilterFromOrigin_ReadsSelectedEventsFromThere(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        LoggedEvent[] streamEvents = [.. _loggedEvents.Where(e => e.StreamId == StreamId)];
        var originIndex = streamEvents[3].Index;
        var origin = ReadOrigin.At(_log[originIndex].Context.StreamPosition);
        var options = new ReadStreamOptions { Filter = EventFilter.EventNames(nameof(OrderShipped)) };
        LoggedEvent[] expected = [.. streamEvents.Where(e => e.EventName == nameof(OrderShipped))];

        var forward = await _eventStore
            .ReadStream(StreamId, ReadDirection.Forward, origin, options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(forward, expected.Where(e => e.Index >= originIndex));

        var backward = await _eventStore
            .ReadStream(StreamId, ReadDirection.Backward, origin, options)
            .ToArrayAsync(cancellationToken);

        ShouldBe(backward, expected.Where(e => e.Index <= originIndex).Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithMetadataFilterButNotIncludingMetadata_ReadsSelectedEventsWithoutMetadata(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        var options = new ReadAllOptions { Filter = EventFilter.Metadata("tenant", "acme"), IncludeMetadata = false };

        var read = await _eventStore.ReadAll(options: options).ToArrayAsync(cancellationToken);

        ShouldBe(read, _loggedEvents.Where(e => e.Tenant == "acme"));
        read.ShouldAllBe(x => x.Context.Metadata.Count == 0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithFilterSelectingNothingInExistingStream_ReadsNothingAndDoesNotThrow(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        var options = new ReadStreamOptions
        {
            Filter = EventFilter.EventNames(nameof(InvoiceRaised)),
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        var read = await _eventStore.ReadStream(StreamId, options: options).ToArrayAsync(cancellationToken);

        read.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithFilterWhenStreamDoesNotExist_Throws(CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.FilteredReads);

        var options = new ReadStreamOptions
        {
            Filter = EventFilter.EventNames(nameof(OrderPlaced)),
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        await Should.ThrowAsync<StreamNotFoundException>(async () =>
            await _eventStore.ReadStream("no-such-stream", options: options).ToArrayAsync(cancellationToken));
    }

    private LoggedEvent[] Expected(EventFilterCase filterCase) =>
        [.. _loggedEvents.Where(e => filterCase.Expected(_midpoint, e))];

    // Events are told apart by their position in the log. The payload is compared as well, as it is what was decoded.
    private void ShouldBe(
        IReadOnlyList<ReadEvent<object, string, TStreamPos, TLogPos>> read,
        IEnumerable<LoggedEvent> expected)
    {
        ReadEvent<object, string, TStreamPos, TLogPos>[] expectedEvents = [.. expected.Select(e => _log[e.Index])];

        read.Select(x => x.Context.LogPosition).ShouldBe(expectedEvents.Select(x => x.Context.LogPosition));
        read.Select(x => x.Payload).ShouldBe(expectedEvents.Select(x => x.Payload));
    }
}