using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoEventStoreTests
{
    [Test]
    public async Task Test()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDb = client.GetDatabase("test");
        var mongoOptions = MongoEventStoreOptions.CreateDefault();
        await MongoEventStoreAdmin.EnsureIndexesAsync(mongoDb, mongoOptions);
        var eventStore = MongoEventStore.Create(mongoDb, mongoOptions);

        NewEventRecord<BsonDocument>[] events =
        [
            new(new NewEventHeader("TestEvent1"),
                new BsonDocument
                {
                    { "foo", "abc" },
                    { "bar", 123 }
                })
        ];

        var expectedVersion = ExpectedStreamVersion.Any;
        await eventStore.AppendToStreamAsync("test-mongo-stream", events, expectedVersion);
        //await eventStore.AppendToStreamAsync("test-stream", events2, expectedVersion + events.Length);

        var result = await eventStore.ReadStreamAsync("test-stream");

        var readEvents = await result.Events.ToListAsync();
    }
}