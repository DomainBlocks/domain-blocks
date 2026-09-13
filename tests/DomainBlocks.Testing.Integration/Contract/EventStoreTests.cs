using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.Contract;

public abstract class EventStoreTests<TStreamPos, TLogPos>(IEventStoreHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

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

    private static IEnumerable<TestCaseData> DirectionAndOriginCases
    {
        get
        {
            yield return new TestCaseData(ReadDirection.Forward, ReadOrigin.Start<TStreamPos>());
            yield return new TestCaseData(ReadDirection.Backward, ReadOrigin.Start<TStreamPos>());
            yield return new TestCaseData(ReadDirection.Forward, ReadOrigin.End<TStreamPos>());
            yield return new TestCaseData(ReadDirection.Backward, ReadOrigin.End<TStreamPos>());
        }
    }

    private static IEnumerable<TestCaseData> DirectionAndOriginEdgeCases
    {
        get
        {
            yield return new TestCaseData(ReadDirection.Forward, ReadOrigin.End<TStreamPos>());
            yield return new TestCaseData(ReadDirection.Backward, ReadOrigin.Start<TStreamPos>());
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ExpectedStateIsAnyAndStreamDoesNotExist_AppendsEvents(
        CancellationToken cancellationToken)
    {
        TestEvent[] events =
        [
            new() { Value = "TestEvent1" },
            new() { Value = "TestEvent2" },
            new() { Value = "TestEvent3" }
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);
        readEvents.Select(x => x.Payload).ShouldBe(events);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ExpectedStateIsAnyAndStreamExists_AppendsEvents(CancellationToken cancellationToken)
    {
        TestEvent[] events1 =
        [
            new() { Value = "TestEvent1" },
            new() { Value = "TestEvent2" },
            new() { Value = "TestEvent3" }
        ];

        TestEvent[] events2 =
        [
            new() { Value = "TestEvent4" },
            new() { Value = "TestEvent5" },
            new() { Value = "TestEvent6" }
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(streamId, events1, cancellationToken: cancellationToken);
        await EventStore.AppendAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);
        readEvents.Select(x => x.Payload).ShouldBe(events1.Concat(events2));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ExpectedStateHasWrongVersion_ThrowsVersionConflict(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(
            streamId,
            [
                new TestEvent { Value = "TestEvent1" },
                new TestEvent { Value = "TestEvent2" },
                new TestEvent { Value = "TestEvent3" }
            ],
            cancellationToken: cancellationToken);

        var expectedState = ExpectedStreamState.AtVersion(CreateStreamPosition(1));

        var exception = await EventStore
            .AppendAsync(
                streamId,
                [new TestEvent { Value = "TestEvent4" }],
                expectedState,
                cancellationToken: cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException<TStreamPos>>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(expectedState);
        exception.ObservedState.ShouldNotBeNull();
        exception.ObservedState.ShouldBe(ObservedStreamState.AtVersion(CreateStreamPosition(2)));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ExpectedStateIsStreamExistsAndStreamDoesNotExist_ThrowsExpectedStreamToExist(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var exception = await EventStore
            .AppendAsync(
                streamId,
                [
                    new TestEvent { Value = "TestEvent1" },
                    new TestEvent { Value = "TestEvent2" },
                    new TestEvent { Value = "TestEvent3" }
                ],
                ExpectedStreamState.Exists<TStreamPos>(),
                cancellationToken: cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException<TStreamPos>>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.Exists<TStreamPos>());
        exception.ObservedState.ShouldNotBeNull();
        exception.ObservedState.ShouldBe(ObservedStreamState.DoesNotExist<TStreamPos>());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ExpectedStateIsStreamDoesNotExistAndStreamExists_ThrowsExpectedStreamToNotExist(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(
            streamId,
            [
                new TestEvent { Value = "TestEvent1" },
                new TestEvent { Value = "TestEvent2" },
                new TestEvent { Value = "TestEvent3" }
            ],
            cancellationToken: cancellationToken);

        var exception = await EventStore
            .AppendAsync(
                streamId,
                [new TestEvent { Value = "TestEvent4" }],
                ExpectedStreamState.DoesNotExist<TStreamPos>(),
                cancellationToken: cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException<TStreamPos>>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.DoesNotExist<TStreamPos>());
        exception.ObservedState.ShouldNotBeNull();
        exception.ObservedState.ShouldBe(ObservedStreamState.AtVersion(CreateStreamPosition(2)));
    }

    [TestCaseSource(nameof(DirectionAndOriginCases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_StreamDoesNotExistAndBehaviorIsThrow_ThrowsStreamNotFound(
        ReadDirection direction,
        ReadOrigin<TStreamPos> origin,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var options = new ReadStreamOptions
        {
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        // The stream must be materialized for the store to notice that it does not exist.
        await EventStore
            .ReadStream(streamId, direction, origin, options)
            .ToArrayAsync(cancellationToken)
            .AsTask()
            .ShouldThrowAsync<StreamNotFoundException>();
    }

    [TestCaseSource(nameof(DirectionAndOriginEdgeCases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_EdgeCaseDirectionAndOriginAndStreamExists_ReturnsEmpty(
        ReadDirection direction,
        ReadOrigin<TStreamPos> origin,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(
            streamId,
            [
                new TestEvent { Value = "TestEvent1" },
                new TestEvent { Value = "TestEvent2" },
                new TestEvent { Value = "TestEvent3" }
            ],
            cancellationToken: cancellationToken);

        var readEvents = await EventStore
            .ReadStream(streamId, direction, origin)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(DirectionAndOriginEdgeCases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_EdgeCasePositionAndDirectionAndStreamDoesNotExist_ReturnsEmpty(
        ReadDirection direction,
        ReadOrigin<TStreamPos> origin,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var readEvents = await EventStore
            .ReadStream(streamId, direction, origin)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_FromPosition_ReturnsExpectedEvents(CancellationToken cancellationToken)
    {
        TestEvent[] events1 =
        [
            new() { Value = "TestEvent1" },
            new() { Value = "TestEvent2" },
            new() { Value = "TestEvent3" }
        ];

        TestEvent[] events2 =
        [
            new() { Value = "TestEvent4" },
            new() { Value = "TestEvent5" },
            new() { Value = "TestEvent6" }
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(streamId, events1, cancellationToken: cancellationToken);
        await EventStore.AppendAsync(streamId, events2, cancellationToken: cancellationToken);

        var expected = events1.Concat(events2).ToArray();

        for (var pos = 0; pos < expected.Length; pos++)
        {
            var actual = await ReadEvents(ReadDirection.Forward, pos);
            actual.ShouldBe(expected.Skip(pos));
        }

        for (var pos = expected.Length - 1; pos >= 0; pos--)
        {
            var actual = await ReadEvents(ReadDirection.Backward, pos);
            actual.ShouldBe(expected.Take(pos + 1).Reverse());
        }

        return;

        ValueTask<object[]> ReadEvents(ReadDirection direction, int position)
        {
            return EventStore
                .ReadStream(
                    streamId,
                    direction,
                    ReadOrigin.At(CreateStreamPosition((ulong)position)))
                .Select(x => x.Payload)
                .ToArrayAsync(cancellationToken);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SameCommitIdTwice_WritesOnce(CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.IdempotentAppends);

        var streamId = NewStreamId();
        var commitId = Guid.NewGuid();
        TestEvent[] events = [new() { Value = "TestEvent1" }];

        await EventStore.AppendAsync(streamId, events, commitId: commitId, cancellationToken: cancellationToken);
        await EventStore.AppendAsync(streamId, events, commitId: commitId, cancellationToken: cancellationToken);

        var readEvents = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.Select(x => x.Payload).ShouldBe(events);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_EmptyEvents_WritesNothing(CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();

        await EventStore.AppendAsync(streamId, Array.Empty<TestEvent>(), cancellationToken: cancellationToken);

        (await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_WithMetadata_RoundTripsMetadata(CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();

        await EventStore.AppendAsync(
            streamId,
            [
                AppendableEvent.Create<object>(new TestEvent { Value = "with" }, [new("tenant", "acme")]),
                AppendableEvent.Create<object>(new TestEvent { Value = "without" })
            ],
            cancellationToken: cancellationToken);

        var readEvents = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents[0].Context.Metadata["tenant"].ShouldBe("acme");
        readEvents[1].Context.Metadata.ShouldBeEmpty();
    }

    [Test]
    public async Task AppendAsync_CallerCancels_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await EventStore
            .AppendAsync(NewStreamId(), [new TestEvent { Value = "TestEvent1" }], cancellationToken: cts.Token)
            .ShouldThrowAsync<OperationCanceledException>();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_ManyEvents_ReadsAllInOrderInBothDirections(CancellationToken cancellationToken)
    {
        var (streamId, events) = await AppendEventsAsync(50, cancellationToken);

        var forward = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);
        forward.Select(x => x.Payload).ShouldBe(events);
        forward.Select(x => x.Context.StreamPosition).ShouldBe(StreamPositions(0, 50));

        var backward = await EventStore.ReadStream(streamId, ReadDirection.Backward).ToArrayAsync(cancellationToken);
        backward.Select(x => x.Payload).ShouldBe(events.Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithMaxCount_ReturnsFirstMaxCountEvents(CancellationToken cancellationToken)
    {
        var (streamId, events) = await AppendEventsAsync(30, cancellationToken);

        var read = await EventStore
            .ReadStream(streamId, options: new ReadStreamOptions { MaxCount = 17 })
            .ToArrayAsync(cancellationToken);

        read.Select(x => x.Payload).ShouldBe(events.Take(17));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_FromPositionInLongStream_ReturnsExpectedEvents(CancellationToken cancellationToken)
    {
        var (streamId, events) = await AppendEventsAsync(20, cancellationToken);

        var forward = await EventStore
            .ReadStream(streamId, origin: ReadOrigin.At(CreateStreamPosition(5)))
            .ToArrayAsync(cancellationToken);

        forward.Select(x => x.Payload).ShouldBe(events.Skip(5));

        var backward = await EventStore
            .ReadStream(streamId, ReadDirection.Backward, ReadOrigin.At(CreateStreamPosition(15)))
            .ToArrayAsync(cancellationToken);

        backward.Select(x => x.Payload).ShouldBe(events.Take(16).Reverse());
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_IncludeMetadataFalse_ReturnsEmptyMetadata(CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();

        await EventStore.AppendAsync(
            streamId,
            [AppendableEvent.Create<object>(new TestEvent { Value = "a" }, [new("tenant", "acme")])],
            cancellationToken: cancellationToken);

        var withMetadata = await EventStore.ReadStream(streamId).SingleAsync(cancellationToken);
        withMetadata.Context.Metadata["tenant"].ShouldBe("acme");

        var withoutMetadata = await EventStore
            .ReadStream(streamId, options: new ReadStreamOptions { IncludeMetadata = false })
            .SingleAsync(cancellationToken);

        withoutMetadata.Context.Metadata.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_AtPositionBeyondEndWithThrow_ReturnsEmpty(CancellationToken cancellationToken)
    {
        var (streamId, _) = await AppendEventsAsync(3, cancellationToken);

        var read = await EventStore
            .ReadStream(
                streamId,
                origin: ReadOrigin.At(CreateStreamPosition(10)),
                options: new ReadStreamOptions { StreamNotFoundBehavior = StreamNotFoundBehavior.Throw })
            .ToArrayAsync(cancellationToken);

        read.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_Always_PreservesEventContext(CancellationToken cancellationToken)
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var (streamId, _) = await AppendEventsAsync(2, cancellationToken);

        var read = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        read.ShouldAllBe(x => x.Context.StreamId == streamId);
        read.Select(x => x.Context.StreamPosition).ShouldBe(StreamPositions(0, 2));
        read.ShouldAllBe(x => x.Context.CreatedAt > before && x.Context.CreatedAt.Offset == TimeSpan.Zero);
    }

    private async Task<(string StreamId, TestEvent[] Events)> AppendEventsAsync(
        int count,
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"e{i}" }).ToArray();

        await EventStore.AppendAsync(streamId, events, cancellationToken: cancellationToken);

        return (streamId, events);
    }

    private IEnumerable<TStreamPos> StreamPositions(int from, int count) =>
        Enumerable.Range(from, count).Select(i => CreateStreamPosition((ulong)i));

    private static string NewStreamId() => $"test-{Guid.NewGuid():N}";
}