using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTests
{
    private const int TestTimeoutMillis = 5_000;

    private IEventStoreClient<IDomainEvent> _client = null!;

    private static IEnumerable<TestCaseData> PositionAndDirectionCases
    {
        get
        {
            yield return new TestCaseData(StreamReadPosition.Start, StreamReadDirection.Forward);
            yield return new TestCaseData(StreamReadPosition.Start, StreamReadDirection.Backward);
            yield return new TestCaseData(StreamReadPosition.End, StreamReadDirection.Forward);
            yield return new TestCaseData(StreamReadPosition.End, StreamReadDirection.Backward);
        }
    }

    private static IEnumerable<TestCaseData> PositionAndDirectionEdgeCases
    {
        get
        {
            yield return new TestCaseData(StreamReadPosition.Start, StreamReadDirection.Backward);
            yield return new TestCaseData(StreamReadPosition.End, StreamReadDirection.Forward);
        }
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _client = await CreateClientAsync();
    }

    // [OneTimeTearDown]
    // public async Task OneTimeTearDown()
    // {
    //     if (_client is IAsyncDisposable asyncDisposable)
    //         await asyncDisposable.DisposeAsync();
    // }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamDoesNotExist_AppendsEvents(
        CancellationToken cancellationToken)
    {
        AppendEvent<IDomainEvent>[] events =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await _client.AppendToStreamAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await _client
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);
        readEvents.Select(x => x.Event).ShouldBe(events.Select(x => x.Event));
    }

    /*
    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamExists_AppendsEvents(
        CancellationToken cancellationToken)
    {
        AppendEvent<TEventData, TMetadata>[] events1 =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        AppendEvent<TEventData, TMetadata>[] events2 =
        [
            CreateTestEvent("TestEvent4"),
            CreateTestEvent("TestEvent5"),
            CreateTestEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await _connection.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await _connection.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await _connection
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);

        readEvents
            .Select(x => x.EventName)
            .ShouldBe(events1.Concat(events2).Select(x => x.EventName));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsWrongVersion_ThrowsVersionConflict(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _connection.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await _connection
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions
                {
                    ExpectedState = ExpectedStreamState.SpecificVersion(new StreamVersion(1))
                },
                cancellationToken)
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        exception.Reason.ShouldBe(WrongExpectedStreamStateReason.VersionConflict);
        exception.ActualVersion.ShouldBe(new StreamVersion(2));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsStreamExistsAndStreamDoesNotExist_ThrowsExpectedStreamToExist(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var exception = await _connection
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

        await _connection.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await _connection
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
        StreamReadPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _connection.AppendToStreamAsync(
            streamId,
            [CreateTestEvent("TestEvent1"), CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await _connection
            .ReadStreamAsync(streamId, options, cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_EdgeCasePositionAndDirectionAndStreamDoesNotExist_ReturnsEmpty(
        StreamReadPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await _connection
            .ReadStreamAsync(streamId, options, cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_StreamDoesNotExistAndBehaviorIsThrow_ThrowsStreamNotFound(
        StreamReadPosition position,
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

        await _connection
            .ReadStreamAsync(streamId, options, cancellationToken)
            // Stream must be materialized.
            .ToArrayAsync(cancellationToken)
            .AsTask()
            .ShouldThrowAsync<StreamNotFoundException>();
    }
    */

    protected abstract Task<IEventStoreClient<IDomainEvent>> CreateClientAsync();

    private static AppendEvent<IDomainEvent> CreateTestEvent(string value)
    {
        return new AppendEvent<IDomainEvent>(new TestEvent { Value = value });
    }

    protected interface IDomainEvent;

    protected record TestEvent : IDomainEvent
    {
        public required string Value { get; init; }
    }
}