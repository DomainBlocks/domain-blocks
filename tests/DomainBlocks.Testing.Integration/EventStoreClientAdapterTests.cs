using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientAdapterTests<TPayload> where TPayload : notnull
{
    private const int TestTimeoutMillis = 5_000;

    private IEventStoreClientAdapter<TPayload> _adapter = null!;

    private static IEnumerable<TestCaseData> PositionAndDirectionCases
    {
        get
        {
            yield return new TestCaseData(StreamPosition.Start, StreamReadDirection.Forward);
            yield return new TestCaseData(StreamPosition.Start, StreamReadDirection.Backward);
            yield return new TestCaseData(StreamPosition.End, StreamReadDirection.Forward);
            yield return new TestCaseData(StreamPosition.End, StreamReadDirection.Backward);
        }
    }

    private static IEnumerable<TestCaseData> PositionAndDirectionEdgeCases
    {
        get
        {
            yield return new TestCaseData(StreamPosition.Start, StreamReadDirection.Backward);
            yield return new TestCaseData(StreamPosition.End, StreamReadDirection.Forward);
        }
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _adapter = await CreateEventStoreAdapterAsync();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamDoesNotExist_AppendsEvents(
        CancellationToken cancellationToken)
    {
        UncommittedEvent<TPayload>[] events =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await _adapter.AppendToStreamAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await _adapter
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(events.Select(x => x.Header.EventName));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamExists_AppendsEvents(
        CancellationToken cancellationToken)
    {
        UncommittedEvent<TPayload>[] events1 =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        UncommittedEvent<TPayload>[] events2 =
        [
            CreateTestEvent("TestEvent4"),
            CreateTestEvent("TestEvent5"),
            CreateTestEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await _adapter.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await _adapter.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await _adapter
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(events1.Concat(events2).Select(x => x.Header.EventName));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsWrongVersion_ThrowsVersionConflict(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _adapter.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await _adapter
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions
                {
                    ExpectedState = ExpectedStreamState.FromVersion(StreamVersion.FromInt64(1))
                },
                cancellationToken)
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        exception.Reason.ShouldBe(WrongExpectedStreamStateReason.VersionConflict);
        exception.ActualVersion.ShouldBe(StreamVersion.FromInt64(2));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsStreamExistsAndStreamDoesNotExist_ThrowsExpectedStreamToExist(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var exception = await _adapter
            .AppendToStreamAsync(
                streamId,
                [
                    CreateTestEvent("TestEvent1"),
                    CreateTestEvent("TestEvent2"),
                    CreateTestEvent("TestEvent3")
                ],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamExists },
                cancellationToken)
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        exception.Reason.ShouldBe(WrongExpectedStreamStateReason.ExpectedStreamToExist);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task
        AppendToStreamAsync_ExpectedStateIsStreamDoesNotExistAndStreamExists_ThrowsExpectedStreamToNotExist(
            CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _adapter.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await _adapter
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamDoesNotExist },
                cancellationToken)
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        exception.Reason.ShouldBe(WrongExpectedStreamStateReason.ExpectedStreamToNotExist);
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_EdgeCasePositionAndDirectionAndStreamExists_ReturnsEmpty(
        StreamPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _adapter.AppendToStreamAsync(
            streamId,
            [CreateTestEvent("TestEvent1"), CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await _adapter
            .ReadStreamAsync(streamId, options, cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_EdgeCasePositionAndDirectionAndStreamDoesNotExist_ReturnsEmpty(
        StreamPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await _adapter
            .ReadStreamAsync(streamId, options, cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_StreamDoesNotExistAndBehaviorIsThrow_ThrowsStreamNotFound(
        StreamPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction,
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        await _adapter
            .ReadStreamAsync(streamId, options, cancellationToken)
            // Stream must be materialized.
            .ToArrayAsync(cancellationToken)
            .AsTask()
            .ShouldThrowAsync<StreamNotFoundException>();
    }

    protected abstract Task<IEventStoreClientAdapter<TPayload>> CreateEventStoreAdapterAsync();

    protected abstract UncommittedEvent<TPayload> CreateTestEvent(string eventName);
}