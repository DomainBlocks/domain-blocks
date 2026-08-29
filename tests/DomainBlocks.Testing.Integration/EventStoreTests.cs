using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreTests<TStreamPos, TLogPos> :
    EventStoreTestBase<object, string, TStreamPos, TLogPos>
    where TStreamPos : notnull
    where TLogPos : notnull
{
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
        AppendableEvent<object>[] events =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);
        readEvents.Select(x => x.Payload).ShouldBe(events.Select(x => x.Payload));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_ExpectedStateIsAnyAndStreamExists_AppendsEvents(CancellationToken cancellationToken)
    {
        AppendableEvent<object>[] events1 =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        AppendableEvent<object>[] events2 =
        [
            CreateTestEvent("TestEvent4"),
            CreateTestEvent("TestEvent5"),
            CreateTestEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await EventStore.AppendAsync(streamId, events1, cancellationToken: cancellationToken);
        await EventStore.AppendAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await EventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);

        readEvents
            .Select(x => x.Payload)
            .ShouldBe(events1.Concat(events2).Select(x => x.Payload));
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
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var expectedState = ExpectedStreamState.AtVersion(CreateStreamPosition(1));

        var exception = await EventStore
            .AppendAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                expectedState,
                cancellationToken: cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException<TStreamPos>>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(expectedState);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.AtVersion(CreateStreamPosition(2)));
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
                    CreateTestEvent("TestEvent1"),
                    CreateTestEvent("TestEvent2"),
                    CreateTestEvent("TestEvent3")
                ],
                ExpectedStreamState.Exists<TStreamPos>(),
                cancellationToken: cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException<TStreamPos>>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.Exists<TStreamPos>());
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.DoesNotExist<TStreamPos>());
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
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await EventStore
            .AppendAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                ExpectedStreamState.DoesNotExist<TStreamPos>(),
                cancellationToken: cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException<TStreamPos>>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.DoesNotExist<TStreamPos>());
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.AtVersion(CreateStreamPosition(2)));
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

        await EventStore
            .ReadStream(streamId, direction, origin, options)
            // Stream must be materialized.
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
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2")
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
        object[] events1 =
        [
            CreateTestEvent("TestEvent1").Payload,
            CreateTestEvent("TestEvent2").Payload,
            CreateTestEvent("TestEvent3").Payload
        ];

        object[] events2 =
        [
            CreateTestEvent("TestEvent4").Payload,
            CreateTestEvent("TestEvent5").Payload,
            CreateTestEvent("TestEvent6").Payload
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
                    ReadOrigin.Position(CreateStreamPosition((ulong)position)))
                .Select(x => x.Payload)
                .ToArrayAsync(cancellationToken);
        }
    }

    private static AppendableEvent<object> CreateTestEvent(string value)
    {
        return new AppendableEvent<object>(new TestEvent { Value = value });
    }
}