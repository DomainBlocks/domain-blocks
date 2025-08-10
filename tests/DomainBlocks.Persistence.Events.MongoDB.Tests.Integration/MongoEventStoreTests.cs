using DomainBlocks.Persistence.Events.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.Persistence.Events.MongoDB.Tests.Integration;

public class MongoEventStoreTests
{
    [Test]
    public async Task Test()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<BsonDocument>("events");
        var eventStore = new MongoEventStore(collection);

        EventData<BsonDocument>[] events =
        [
            new("TestEvent1",
                new BsonDocument
                {
                    { "foo", "abc" },
                    { "bar", 123 }
                },
                new Dictionary<string, string>
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                }),

            new("TestEvent2",
                new BsonDocument
                {
                    { "foo", "abc" },
                    { "bar", 123 }
                },
                new Dictionary<string, string>
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                })
        ];

        long? expectedVersion = null;
        await eventStore.AppendToStreamAsync("test-stream", events, expectedVersion);
        await eventStore.AppendToStreamAsync("test-stream", events, expectedVersion + events.Length);

        var result = await eventStore.ReadStreamAsync("test-stream");

        var readEvents = await result.Events.ToListAsync();
    }
}