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

        const string streamId = "test-mongo-stream1";
        var expectedVersion = ExpectedStreamState.StreamExists;

        try
        {
            await eventStore.AppendToStreamAsync(streamId, events, expectedVersion);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        await eventStore.AppendToStreamAsync(streamId, events, ExpectedStreamState.Any);

        var result = await eventStore.ReadStreamAsync(streamId);

        var readEvents = await result.Events.ToListAsync();
    }
}