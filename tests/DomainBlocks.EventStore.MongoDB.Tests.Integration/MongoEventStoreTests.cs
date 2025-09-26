using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreTests : MongoEventStoreTestFixture<BsonDocument>
{
    private const int TestTimeoutMillis = 5_000;

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamDoesNotExist_AppendsEventsToStream(
        CancellationToken cancellationToken)
    {
        UncommittedEvent<BsonDocument>[] events =
        [
            CreateEvent("TestEvent1"),
            CreateEvent("TestEvent2"),
            CreateEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await MongoEventStore.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any,
            cancellationToken);

        var readResult = await MongoEventStore.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
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
        UncommittedEvent<BsonDocument>[] newEvents1 =
        [
            CreateEvent("TestEvent1"),
            CreateEvent("TestEvent2"),
            CreateEvent("TestEvent3")
        ];

        UncommittedEvent<BsonDocument>[] newEvents2 =
        [
            CreateEvent("TestEvent4"),
            CreateEvent("TestEvent5"),
            CreateEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await MongoEventStore.AppendToStreamAsync(
            streamId,
            newEvents1,
            ExpectedStreamState.Any,
            cancellationToken);

        await MongoEventStore.AppendToStreamAsync(
            streamId,
            newEvents2,
            ExpectedStreamState.Any,
            cancellationToken);

        var readResult = await MongoEventStore.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
        var readEvents = await readResult.Events.ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(newEvents1.Concat(newEvents2).Select(x => x.Header.EventName));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task ReadStreamAsync_FromStartBackwardWhenStreamExists_ReturnsEmptySuccess(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await MongoEventStore.AppendToStreamAsync(
            streamId,
            [CreateEvent("TestEvent1")],
            cancellationToken: cancellationToken);

        var options = new ReadStreamOptions
        {
            Position = StreamPosition.Start,
            Direction = StreamReadDirection.Backward
        };

        var readResult = await MongoEventStore.ReadStreamAsync(streamId, options, cancellationToken);
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

        var readResult = await MongoEventStore.ReadStreamAsync(streamId, options, cancellationToken);
        var events = await readResult.Events.ToArrayAsync(cancellationToken);

        readResult.Status.ShouldBe(ReadStreamStatus.StreamNotFound);
        events.ShouldBeEmpty();
    }

    private static UncommittedEvent<BsonDocument> CreateEvent(string eventName)
    {
        return UncommittedEvent.Create(new UncommittedEventHeader(eventName), new BsonDocument());
    }
}