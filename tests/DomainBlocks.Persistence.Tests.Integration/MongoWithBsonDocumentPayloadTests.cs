using DomainBlocks.Persistence.Events;
using DomainBlocks.Persistence.Mongo.Events;
using DomainBlocks.Serialization.Events;
using DomainBlocks.Serialization.MongoBson;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Persistence.Tests.Integration;

public class MongoWithBsonDocumentPayloadTests
{
    [Test]
    public async Task Should_write_and_read_event()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<BsonDocument>("events");

        var eventDataStore = new MongoEventDataStore<BsonDocument>(
            collection,
            x => x,
            x => x.AsBsonDocument);

        EventTypeMapping[] mappings =
        [
            new(typeof(UserCreated))
        ];

        var serializer = new MongoBsonDocumentSerializer();
        var eventSerializer = new EventSerializer<BsonDocument>(mappings, serializer);
        var eventStore = new EventStore<object, BsonDocument>(eventDataStore, eventSerializer);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-bson-stream-{Guid.NewGuid()}";

        await eventStore.AppendToStreamAsync(streamId, [originalEvent]);

        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvent = await result.Events.ToArrayAsync();

        readEvent
            .ShouldHaveSingleItem()
            .ShouldBeOfType<UserCreated>()
            .ShouldBe(originalEvent);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}