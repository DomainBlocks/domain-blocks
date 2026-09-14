using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.Contract;

/// <summary>
/// Reading the whole log: order, positions, origins and options.
/// </summary>
public abstract class EventStoreReadAllTests<TStreamPos, TLogPos>(IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

    // Every test reads the whole log, so each starts from an empty one.
    protected override bool ResetLogBeforeEachTest => true;

    [SetUp]
    public void SetUp()
    {
        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());
        EventStore = CreateEventStore(eventTypeMap);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (EventStore is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_Forward_ReturnsAllEventsInLogOrder(CancellationToken cancellationToken)
    {
        var events = await AppendAcrossStreamsAsync(50, cancellationToken);

        var read = await EventStore.ReadAll().ToArrayAsync(cancellationToken);

        read.Select(x => x.Payload).ShouldBe(events);
        read.Select(x => x.Context.LogPosition).ShouldBe(LogPositions(0, 50));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_Backward_ReturnsAllEventsInReverseLogOrder(CancellationToken cancellationToken)
    {
        var events = await AppendAcrossStreamsAsync(20, cancellationToken);

        var read = await EventStore.ReadAll(ReadDirection.Backward).ToArrayAsync(cancellationToken);

        read.Select(x => x.Payload).ShouldBe(events.Reverse());
        read.Select(x => x.Context.LogPosition).ShouldBe(LogPositions(0, 20).Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_AtPosition_IsInclusiveInBothDirections(CancellationToken cancellationToken)
    {
        var events = await AppendAcrossStreamsAsync(20, cancellationToken);

        var forward = await EventStore
            .ReadAll(origin: ReadOrigin.At(CreateLogPosition(8)))
            .ToArrayAsync(cancellationToken);

        forward.Select(x => x.Payload).ShouldBe(events.Skip(8));

        var backward = await EventStore
            .ReadAll(ReadDirection.Backward, ReadOrigin.At(CreateLogPosition(8)))
            .ToArrayAsync(cancellationToken);

        backward.Select(x => x.Payload).ShouldBe(events.Take(9).Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithMaxCount_ReturnsFirstMaxCountEvents(CancellationToken cancellationToken)
    {
        var events = await AppendAcrossStreamsAsync(30, cancellationToken);

        var read = await EventStore
            .ReadAll(options: new ReadAllOptions { MaxCount = 17 })
            .ToArrayAsync(cancellationToken);

        read.Select(x => x.Payload).ShouldBe(events.Take(17));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_MultipleStreams_InterleavesInCommitOrder(CancellationToken cancellationToken)
    {
        await EventStore.AppendAsync("a", [Appendable("a0"), Appendable("a1")], cancellationToken: cancellationToken);
        await EventStore.AppendAsync("b", [Appendable("b0")], cancellationToken: cancellationToken);
        await EventStore.AppendAsync("a", [Appendable("a2")], cancellationToken: cancellationToken);

        var read = await EventStore.ReadAll().ToArrayAsync(cancellationToken);

        read.Select(x => (x.Context.StreamId, x.Context.StreamPosition, x.Context.LogPosition)).ShouldBe(
        [
            ("a", CreateStreamPosition(0), CreateLogPosition(0)),
            ("a", CreateStreamPosition(1), CreateLogPosition(1)),
            ("b", CreateStreamPosition(0), CreateLogPosition(2)),
            ("a", CreateStreamPosition(2), CreateLogPosition(3))
        ]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_IncludeMetadataFalse_ReturnsEmptyMetadata(CancellationToken cancellationToken)
    {
        await EventStore.AppendAsync(
            "s",
            [AppendableEvent.Create<object>(new TestEvent { Value = "a" }, [new("tenant", "acme")])],
            cancellationToken: cancellationToken);

        var withMetadata = await EventStore.ReadAll().SingleAsync(cancellationToken);
        withMetadata.Context.Metadata["tenant"].ShouldBe("acme");

        var withoutMetadata = await EventStore
            .ReadAll(options: new ReadAllOptions { IncludeMetadata = false })
            .SingleAsync(cancellationToken);

        withoutMetadata.Context.Metadata.ShouldBeEmpty();
    }

    [TestCase(ReadDirection.Forward, true)]
    [TestCase(ReadDirection.Backward, false)]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_EdgeCaseDirectionAndOrigin_ReturnsEmpty(
        ReadDirection direction,
        bool fromEnd,
        CancellationToken cancellationToken)
    {
        await AppendAcrossStreamsAsync(3, cancellationToken);

        ReadOrigin<TLogPos> origin = fromEnd ? ReadOrigin.End<TLogPos>() : ReadOrigin.Start<TLogPos>();

        var read = await EventStore.ReadAll(direction, origin).ToArrayAsync(cancellationToken);

        read.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_EmptyLog_ReturnsEmpty(CancellationToken cancellationToken)
    {
        (await EventStore.ReadAll().ToArrayAsync(cancellationToken)).ShouldBeEmpty();
        (await EventStore.ReadAll(ReadDirection.Backward).ToArrayAsync(cancellationToken)).ShouldBeEmpty();
    }

    /// <summary>
    /// Appends one event per call, round-robin over three streams, so that the log interleaves streams.
    /// </summary>
    private async Task<TestEvent[]> AppendAcrossStreamsAsync(int count, CancellationToken cancellationToken)
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"e{i}" }).ToArray();

        foreach (var (e, i) in events.Select((e, i) => (e, i)))
            await EventStore.AppendAsync($"s{i % 3}", [Appendable(e)], cancellationToken: cancellationToken);

        return events;
    }

    private IEnumerable<TLogPos> LogPositions(int from, int count) =>
        Enumerable.Range(from, count).Select(i => CreateLogPosition((ulong)i));

    private static AppendableEvent<object> Appendable(string value) => Appendable(new TestEvent { Value = value });

    private static AppendableEvent<object> Appendable(TestEvent e) => AppendableEvent.Create<object>(e);
}