using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Persistence.Mongo.Events;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.Persistence.MongoDB.Tests.Integration.Events;

public class MongoEventDataStoreTests
{
    [Test]
    public async Task Test()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<BsonDocument>("events");
        var eventDataStore = new MongoEventDataStore(collection);

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
        await eventDataStore.AppendToStreamAsync("test-stream", events, expectedVersion);
        await eventDataStore.AppendToStreamAsync("test-stream", events, expectedVersion + events.Length);

        var result = await eventDataStore.ReadStreamAsync("test-stream");

        var readEvents = await result.Events.ToListAsync();
    }
}