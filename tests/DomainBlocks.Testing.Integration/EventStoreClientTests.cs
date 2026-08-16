using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

using StreamReadPosition = ReadPosition<StreamPosition>;

public abstract class EventStoreClientTests : EventStoreClientTestBase<object>
{
    private static IEnumerable<TestCaseData> PositionAndDirectionCases
    {
        get
        {
            yield return new TestCaseData(StreamReadPosition.Start, ReadDirection.Forward);
            yield return new TestCaseData(StreamReadPosition.Start, ReadDirection.Backward);
            yield return new TestCaseData(StreamReadPosition.End, ReadDirection.Forward);
            yield return new TestCaseData(StreamReadPosition.End, ReadDirection.Backward);
        }
    }

    private static IEnumerable<TestCaseData> PositionAndDirectionEdgeCases
    {
        get
        {
            yield return new TestCaseData(StreamReadPosition.Start, ReadDirection.Backward);
            yield return new TestCaseData(StreamReadPosition.End, ReadDirection.Forward);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamDoesNotExist_AppendsEvents(
        CancellationToken cancellationToken)
    {
        AppendEvent<object>[] events =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await Client.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);
        readEvents.Unwrap().ShouldBe(events.Select(x => x.Event));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsAnyAndStreamExists_AppendsEvents(
        CancellationToken cancellationToken)
    {
        AppendEvent<object>[] events1 =
        [
            CreateTestEvent("TestEvent1"),
            CreateTestEvent("TestEvent2"),
            CreateTestEvent("TestEvent3")
        ];

        AppendEvent<object>[] events2 =
        [
            CreateTestEvent("TestEvent4"),
            CreateTestEvent("TestEvent5"),
            CreateTestEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await Client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await Client.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Context.StreamId == streamId);

        readEvents
            .Unwrap()
            .ShouldBe(events1.Concat(events2).Select(x => x.Event));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendToStreamAsync_ExpectedStateHasWrongVersion_ThrowsVersionConflict(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var expectedState = ExpectedStreamState.SpecificVersion(new StreamPosition(1));

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions
                {
                    ExpectedStreamState = expectedState
                },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(expectedState);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamExists(new StreamPosition(2)));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendToStreamAsync_ExpectedStateIsStreamExistsAndStreamDoesNotExist_ThrowsExpectedStreamToExist(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var exception = await Client
            .AppendToStreamAsync(
                streamId,
                [
                    CreateTestEvent("TestEvent1"),
                    CreateTestEvent("TestEvent2"),
                    CreateTestEvent("TestEvent3")
                ],
                new AppendToStreamOptions { ExpectedStreamState = ExpectedStreamState.StreamExists },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.StreamExists);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamDoesNotExist);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task
        AppendToStreamAsync_ExpectedStateIsStreamDoesNotExistAndStreamExists_ThrowsExpectedStreamToNotExist(
            CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

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
                new AppendToStreamOptions { ExpectedStreamState = ExpectedStreamState.StreamDoesNotExist },
                cancellationToken)
            .ShouldThrowAsync<StreamAppendConflictException>();

        exception.StreamId.ShouldBe(streamId);
        exception.ExpectedState.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
        exception.ActualState.ShouldNotBeNull();
        exception.ActualState.ShouldBe(StreamState.StreamExists(new StreamPosition(2)));
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_EdgeCasePositionAndDirectionAndStreamExists_ReturnsEmpty(
        StreamReadPosition position,
        ReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(
            streamId,
            [CreateTestEvent("TestEvent1"), CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await Client.ReadStream(streamId, options).ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionEdgeCases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_EdgeCasePositionAndDirectionAndStreamDoesNotExist_ReturnsEmpty(
        StreamReadPosition position,
        ReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await Client.ReadStream(streamId, options).ToArrayAsync(cancellationToken);

        readEvents.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(PositionAndDirectionCases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_StreamDoesNotExistAndBehaviorIsThrow_ThrowsStreamNotFound(
        StreamReadPosition position,
        ReadDirection direction,
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction,
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        await Client
            .ReadStream(streamId, options)
            // Stream must be materialized.
            .ToArrayAsync(cancellationToken)
            .AsTask()
            .ShouldThrowAsync<StreamNotFoundException>();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_FromVersion_ReturnsExpectedEvents(CancellationToken cancellationToken)
    {
        object[] events1 =
        [
            CreateTestEvent("TestEvent1").Event,
            CreateTestEvent("TestEvent2").Event,
            CreateTestEvent("TestEvent3").Event
        ];

        object[] events2 =
        [
            CreateTestEvent("TestEvent4").Event,
            CreateTestEvent("TestEvent5").Event,
            CreateTestEvent("TestEvent6").Event
        ];

        var streamId = $"test-{Guid.NewGuid():N}";

        await Client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await Client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var expected = events1.Concat(events2).ToArray();

        // Forward: At(v) == expected.Skip(v)
        for (var v = 0; v < expected.Length; v++)
        {
            var actual = await ReadEvents(v, ReadDirection.Forward);
            actual.ShouldBe(expected.Skip(v));
        }

        // Backward: At(v) == expected.Take(v+1).Reverse()
        for (var v = expected.Length - 1; v >= 0; v--)
        {
            var actual = await ReadEvents(v, ReadDirection.Backward);
            actual.ShouldBe(expected.Take(v + 1).Reverse());
        }

        ValueTask<object[]> ReadEvents(int startVersion, ReadDirection direction)
        {
            return Client
                .ReadStream(
                    streamId,
                    new ReadStreamOptions
                    {
                        Position = StreamReadPosition.At(StreamPosition.FromInt64(startVersion)),
                        Direction = direction
                    })
                .Unwrap()
                .ToArrayAsync(cancellationToken);
        }
    }

    private static AppendEvent<object> CreateTestEvent(string value)
    {
        return new AppendEvent<object>(new TestEvent { Value = value });
    }
}