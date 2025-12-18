using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;
using StreamPosition = DomainBlocks.EventStore.Abstractions.StreamPosition;


namespace DomainBlocks.EventStore.KurrentDb.Tests.Integration;

public class KurrentDbEventStoreAdapterTests
{
    private const int TestTimeoutMillis = 5_000;
    private IKurrentDbEventStoreAdapter _adapter = null!;

    [SetUp]
    public void OneTimeSetUp()
    {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
        var client = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));

        _adapter = new KurrentDbEventStoreAdapter(client);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamDoesNotExist_AppendsEventsToStream(
        CancellationToken cancellationToken)
    {
        UncommittedEvent<ReadOnlyMemory<byte>>[] events =
        [
            TestEventsHelper.CreateTestEvent("TestEvent1"),
            TestEventsHelper.CreateTestEvent("TestEvent2"),
            TestEventsHelper.CreateTestEvent("TestEvent3")
        ];

        var streamId = $"test-{Uuid.NewUuid()}";
        await _adapter.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any,
            cancellationToken);

        var readResult = await _adapter.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
        var readEvents = await readResult.Events.ToArrayAsync(cancellationToken);

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
        UncommittedEvent<ReadOnlyMemory<byte>>[] events =
        [
            TestEventsHelper.CreateTestEvent("TestEvent1"),
            TestEventsHelper.CreateTestEvent("TestEvent2"),
            TestEventsHelper.CreateTestEvent("TestEvent3")
        ];

        UncommittedEvent<ReadOnlyMemory<byte>>[] events2 =
        [
            TestEventsHelper.CreateTestEvent("TestEvent4"),
            TestEventsHelper.CreateTestEvent("TestEvent5"),
            TestEventsHelper.CreateTestEvent("TestEvent6")
        ];

        var streamId = $"test-{Uuid.NewUuid()}";

        await _adapter.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any, cancellationToken);

        await _adapter.AppendToStreamAsync(
            streamId,
            events2,
            ExpectedStreamState.Any, cancellationToken);

        var readResult = await _adapter.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
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

        await _adapter.AppendToStreamAsync(
            streamId,
            [TestEventsHelper.CreateTestEvent("TestEvent1")],
            cancellationToken: cancellationToken);

        await _adapter.AppendToStreamAsync(
            streamId,
            [TestEventsHelper.CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = StreamPosition.Start,
            Direction = StreamReadDirection.Backward
        };

        var readResult = await _adapter.ReadStreamAsync(streamId, options, cancellationToken);
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

        await _adapter.AppendToStreamAsync(
            streamId,
            [TestEventsHelper.CreateTestEvent("TestEvent1")],
            cancellationToken: cancellationToken);

        await _adapter.AppendToStreamAsync(
            streamId,
            [TestEventsHelper.CreateTestEvent("TestEvent2")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = StreamPosition.End,
            Direction = StreamReadDirection.Forward
        };

        var readResult = await _adapter.ReadStreamAsync(streamId, options, cancellationToken);
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

        var readResult = await _adapter.ReadStreamAsync(streamId, options, cancellationToken);
        var events = await readResult.Events.ToArrayAsync(cancellationToken);

        readResult.Status.ShouldBe(ReadStreamStatus.StreamNotFound);
        events.ShouldBeEmpty();
    }
}