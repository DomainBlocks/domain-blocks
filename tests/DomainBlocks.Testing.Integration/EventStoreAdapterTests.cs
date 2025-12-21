using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreAdapterTests<TPayload> where TPayload : notnull
{
    private const int TestTimeoutMillis = 5_000;

    private IEventStoreAdapter<TPayload> _adapter = null!;

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

    private static IEnumerable<TestCaseData> BoundaryPositionAndDirectionCases
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
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamDoesNotExist_AppendsEventsToStream(
        CancellationToken cancellationToken)
    {
        UncommittedEvent<TPayload>[] events =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await _adapter.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any,
            cancellationToken);

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
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamExists_AppendsEventsToStream(
        CancellationToken cancellationToken)
    {
        UncommittedEvent<TPayload>[] events =
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

        await _adapter.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any, cancellationToken);

        await _adapter.AppendToStreamAsync(
            streamId,
            events2,
            ExpectedStreamState.Any, cancellationToken);

        var readEvents = await _adapter
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(events.Concat(events2).Select(x => x.Header.EventName));
    }

    [TestCaseSource(nameof(BoundaryPositionAndDirectionCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromBoundaryPositionAndDirectionWhenStreamExists_ReturnsEmpty(
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

    [TestCaseSource(nameof(BoundaryPositionAndDirectionCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromBoundaryPositionAndDirectionWhenStreamDoesNotExist_ReturnsEmpty(
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
    public async Task ReadStreamAsync_WhenStreamDoesNotExistAndThrowBehaviorUsed_ThrowsStreamNotFoundException(
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

        await Should.ThrowAsync<StreamNotFoundException>(async () =>
        {
            await _adapter
                .ReadStreamAsync(streamId, options, cancellationToken)
                // Stream must be materialized.
                .ToArrayAsync(cancellationToken);
        });
    }

    protected abstract Task<IEventStoreAdapter<TPayload>> CreateEventStoreAdapterAsync();

    protected abstract UncommittedEvent<TPayload> CreateTestEvent(string eventName);
}