using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreTests
{
    private const int TestTimeoutMillis = 5_000;

    private IMongoEventStore<BsonDocument> _mongoEventStore = null!;

    [SetUp]
    public async Task OneTimeSetUp()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDb = client.GetDatabase("test");

        var mongoOptions = MongoEventStoreOptions.CreateDefault(
            eventCollectionName: $"{nameof(MongoEventStoreTests)}.Events");

        await MongoEventStoreAdmin.EnsureIndexesAsync(mongoDb, mongoOptions);
        _mongoEventStore = MongoEventStore.Create(mongoDb, mongoOptions);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamDoesNotExist_AppendsEventsToStream(
        CancellationToken cancellationToken)
    {
        NewEventRecord<BsonDocument>[] newEvents =
        [
            CreateEvent("TestEvent1"),
            CreateEvent("TestEvent2"),
            CreateEvent("TestEvent3")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await _mongoEventStore.AppendToStreamAsync(
            streamId,
            newEvents,
            ExpectedStreamState.Any,
            cancellationToken);

        var readResult = await _mongoEventStore.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
        var readEvents = await readResult.Events.ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(newEvents.Select(x => x.Header.EventName));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenExpectedStateAnyAndStreamExists_AppendsEventsToStream(
        CancellationToken cancellationToken)
    {
        NewEventRecord<BsonDocument>[] newEvents1 =
        [
            CreateEvent("TestEvent1"),
            CreateEvent("TestEvent2"),
            CreateEvent("TestEvent3")
        ];

        NewEventRecord<BsonDocument>[] newEvents2 =
        [
            CreateEvent("TestEvent4"),
            CreateEvent("TestEvent5"),
            CreateEvent("TestEvent6")
        ];

        var streamId = $"test-{Guid.NewGuid()}";

        await _mongoEventStore.AppendToStreamAsync(
            streamId,
            newEvents1,
            ExpectedStreamState.Any,
            cancellationToken);

        await _mongoEventStore.AppendToStreamAsync(
            streamId,
            newEvents2,
            ExpectedStreamState.Any,
            cancellationToken);

        var readResult = await _mongoEventStore.ReadStreamAsync(streamId, cancellationToken: cancellationToken);
        var readEvents = await readResult.Events.ToArrayAsync(cancellationToken);

        readEvents.ShouldAllBe(x => x.Header.StreamId == streamId);

        readEvents
            .Select(x => x.Header.EventName)
            .ShouldBe(newEvents1.Concat(newEvents2).Select(x => x.Header.EventName));
    }

    private static NewEventRecord<BsonDocument> CreateEvent(string eventName)
    {
        return NewEventRecord.Create(new NewEventHeader(eventName), new BsonDocument());
    }
}