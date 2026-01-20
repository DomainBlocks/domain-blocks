using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTests
{
    private const int TestTimeoutMillis = 5_000;

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

    protected abstract IEventStoreClient<IDomainEvent> Client { get; }

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

        await Client.AppendToStreamAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await Client
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);
        readEvents.Unwrap().ShouldBe(events.Select(x => x.Event));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamExists_AppendsEvents(
        CancellationToken cancellationToken)
    {
        AppendEvent<IDomainEvent>[] events1 =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        AppendEvent<IDomainEvent>[] events2 =
        [
            CreateTestEvent("TestEvent4"),
            CreateTestEvent("TestEvent5"),
            CreateTestEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await Client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await Client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await Client
            .ReadStreamAsync(streamId, cancellationToken: cancellationToken)
            .ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);

        readEvents
            .Unwrap()
            .ShouldBe(events1.Concat(events2).Select(x => x.Event));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateHasWrongVersion_ThrowsVersionConflict(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await Client.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var expectedState = ExpectedStreamState.SpecificVersion(new StreamVersion(1));

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions
                {
                    ExpectedState = expectedState
                },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(expectedState);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamExists(new StreamVersion(2)));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsStreamExistsAndStreamDoesNotExist_ThrowsExpectedStreamToExist(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [
                    CreateTestEvent("TestEvent1"),
                    CreateTestEvent("TestEvent2"),
                    CreateTestEvent("TestEvent3")
                ],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamExists },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.StreamExists);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamDoesNotExist);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task
        AppendToStreamAsync_ExpectedStateIsStreamDoesNotExistAndStreamExists_ThrowsExpectedStreamToNotExist(
            CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await Client.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamDoesNotExist },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamExists(new StreamVersion(2)));
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_EdgeCasePositionAndDirectionAndStreamExists_ReturnsEmpty(
        StreamReadPosition position,
        StreamReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await Client.AppendToStreamAsync(
            streamId,
            [CreateTestEvent("TestEvent1"), CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await Client
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

        var readEvents = await Client
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

        await Client
            .ReadStreamAsync(streamId, options, cancellationToken)
            // Stream must be materialized.
            .ToArrayAsync(cancellationToken)
            .AsTask()
            .ShouldThrowAsync<StreamNotFoundException>();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromVersion_ReturnsExpectedEvents(CancellationToken cancellationToken)
    {
        IDomainEvent[] events1 =
        [
            CreateTestEvent("TestEvent1").Event,
            CreateTestEvent("TestEvent2").Event,
            CreateTestEvent("TestEvent3").Event
        ];

        IDomainEvent[] events2 =
        [
            CreateTestEvent("TestEvent4").Event,
            CreateTestEvent("TestEvent5").Event,
            CreateTestEvent("TestEvent6").Event
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await Client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await Client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var expected = events1.Concat(events2).ToArray();

        // Forward: At(v) == expected.Skip(v)
        for (var v = 0; v < expected.Length; v++)
        {
            var actual = await ReadEvents(v, StreamReadDirection.Forward);
            actual.ShouldBe(expected.Skip(v));
        }

        // Backward: At(v) == expected.Take(v+1).Reverse()
        for (var v = expected.Length - 1; v >= 0; v--)
        {
            var actual = await ReadEvents(v, StreamReadDirection.Backward);
            actual.ShouldBe(expected.Take(v + 1).Reverse());
        }

        ValueTask<IDomainEvent[]> ReadEvents(int startVersion, StreamReadDirection direction)
        {
            return Client
                .ReadStreamAsync(
                    streamId,
                    new ReadStreamOptions
                    {
                        Position = StreamReadPosition.At(StreamVersion.FromInt64(startVersion)),
                        Direction = direction
                    },
                    cancellationToken)
                .Unwrap()
                .ToArrayAsync(cancellationToken);
        }
    }

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