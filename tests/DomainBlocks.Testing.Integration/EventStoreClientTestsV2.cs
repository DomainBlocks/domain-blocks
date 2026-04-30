using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTestsV2 : EventStoreClientTestBase<object>
{
    // Set a longer timeout when debugging.
#if DEBUG
    private const int TestTimeoutMillis = 10 * 60 * 1_000;
#else
    private const int TestTimeoutMillis = 120 * 1_000;
#endif

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

    private ITestEventStoreClientFactory<object> _clientFactory = null!;
    private ITestEventStoreClientHandle<object> _clientHandle = null!;
    private IEventStoreClient<object> _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        _clientFactory = await GetClientFactoryAsync();
        _clientHandle = await _clientFactory.CreateAsync();
        _client = _clientHandle.Client;
    }

    [TearDown]
    public async Task TearDown()
    {
        await _clientHandle.DisposeAsync();
        await _clientFactory.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
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

        await _client.AppendToStreamAsync(streamId, events, cancellationToken: cancellationToken);

        var readEvents = await _client
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

        await _client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await _client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

        var readEvents = await _client
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
        var streamId = $"test-{Guid.NewGuid():N}";

        await _client.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var expectedState = ExpectedStreamState.SpecificVersion(new StreamVersion(1));

        var exception = await _client
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
        var streamId = $"test-{Guid.NewGuid():N}";

        var exception = await _client
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
        var streamId = $"test-{Guid.NewGuid():N}";

        await _client.AppendToStreamAsync(
            streamId,
            [
                CreateTestEvent("TestEvent1"),
                CreateTestEvent("TestEvent2"),
                CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await _client
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
        var streamId = $"test-{Guid.NewGuid():N}";

        await _client.AppendToStreamAsync(
            streamId,
            [CreateTestEvent("TestEvent1"), CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await _client
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
        var streamId = $"test-{Guid.NewGuid():N}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction
        };

        var readEvents = await _client
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
        var streamId = $"test-{Guid.NewGuid():N}";

        var options = new ReadStreamOptions
        {
            Position = position,
            Direction = direction,
            StreamNotFoundBehavior = StreamNotFoundBehavior.Throw
        };

        await _client
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

        await _client.AppendToStreamAsync(streamId, events1, cancellationToken: cancellationToken);
        await _client.AppendToStreamAsync(streamId, events2, cancellationToken: cancellationToken);

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

        ValueTask<object[]> ReadEvents(int startVersion, StreamReadDirection direction)
        {
            return _client
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

    private static AppendEvent<object> CreateTestEvent(string value)
    {
        return new AppendEvent<object>(new TestEvent { Value = value });
    }
}