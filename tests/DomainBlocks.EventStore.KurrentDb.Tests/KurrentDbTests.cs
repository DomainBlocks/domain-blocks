
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;
using StreamPosition = DomainBlocks.EventStore.Abstractions.StreamPosition;


namespace DomainBlocks.EventStore.KurrentDb.Tests;

public class KurrentDbTests
{
    private const int TestTimeoutMillis = 5_000;
    private IKurrentDbEventStore _eventStore = null!;

    [SetUp]
    public void OneTimeSetUp()
    {
        var client = new KurrentDBClient(
            KurrentDBClientSettings.Create("kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false")
        );

        _eventStore = new KurrentDbEventStore(client);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamDoesNotExist_AppendsEventsToStream(
        CancellationToken cancellationToken)
    {
        UncommittedEvent<ReadOnlyMemory<byte>>[] events =
        [
           CreateEvent("TestEvent1"),
           CreateEvent("TestEvent2"),
           CreateEvent("TestEvent3")
        ];

        var streamId = $"test-{Uuid.NewUuid()}";
        await _eventStore.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any,
            cancellationToken);

        var readResult = await _eventStore.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
        var readEvents = await readResult.Events.ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(events.Select(x => x.Header.EventName));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamExists_AppendsEventsToStream(CancellationToken cancellationToken)
    {
        UncommittedEvent<ReadOnlyMemory<byte>>[] events =
        [
           CreateEvent("TestEvent1"),
           CreateEvent("TestEvent2"),
           CreateEvent("TestEvent3")
        ];

        UncommittedEvent<ReadOnlyMemory<byte>>[] events2 =
        [
            CreateEvent("TestEvent4"),
            CreateEvent("TestEvent5"),
            CreateEvent("TestEvent6")
        ];

        var streamId = $"test-{Uuid.NewUuid()}";

        await _eventStore.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any, cancellationToken);

        await _eventStore.AppendToStreamAsync(
            streamId,
            events2,
            ExpectedStreamState.Any, cancellationToken);

        var readResult = await _eventStore.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
        var readEvents = await readResult.Events.ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(events.Concat(events2).Select(x => x.Header.EventName));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromStartBackwardWhenStreamExists_ReturnsEmptySuccess(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _eventStore.AppendToStreamAsync(
            streamId,
            [CreateEvent("TestEvent1")],
            cancellationToken: cancellationToken);
        await _eventStore.AppendToStreamAsync(
            streamId,
            [CreateEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = StreamPosition.Start,
            Direction = StreamReadDirection.Backward
        };

        var readResult = await _eventStore.ReadStreamAsync(streamId, options, cancellationToken);
        var events = await readResult.Events.ToArrayAsync(cancellationToken);

        readResult.Status.ShouldBe(ReadStreamStatus.Success);
        events.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromEndForwardsWhenStreamExists_ReturnsEmptySuccess(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _eventStore.AppendToStreamAsync(
            streamId,
            [CreateEvent("TestEvent1")],
            cancellationToken: cancellationToken);
        await _eventStore.AppendToStreamAsync(
            streamId,
            [CreateEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = StreamPosition.End,
            Direction = StreamReadDirection.Forward
        };

        var readResult = await _eventStore.ReadStreamAsync(streamId, options, cancellationToken);
        var events = await readResult.Events.ToArrayAsync(cancellationToken);

        readResult.Status.ShouldBe(ReadStreamStatus.Success);
        events.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromStartBackwardWhenStreamDoesNotExist_ReturnsNotFound(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";
        var options = new ReadStreamOptions
        {
            Position = StreamPosition.Start,
            Direction = StreamReadDirection.Backward
        };

        var readResult = await _eventStore.ReadStreamAsync(streamId, options, cancellationToken);
        var events = await readResult.Events.ToArrayAsync(cancellationToken);

        readResult.Status.ShouldBe(ReadStreamStatus.StreamNotFound);
        events.ShouldBeEmpty();
    }

    internal static UncommittedEvent<ReadOnlyMemory<byte>> CreateEvent(string eventName)
    {
        var payload = new Dictionary<string, string>
        {
            { "TestProperty", "TestValue" }
        };
        var header = new UncommittedEventHeader(eventName);
        return UncommittedEvent.Create(header, payload.SerializeToUtf8Json());
    }
}