using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class MongoWithBsonDocumentPayloadTests
{
    [Test]
    public async Task Should_write_and_read_event_with_transform()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDb = client.GetDatabase("test");
        var mongoOptions = MongoEventStoreOptions.CreateDefault();
        await MongoEventStore.EnsureIndexesAsync(mongoDb, mongoOptions);

        var eventStoreOptions = new EventStoreOptions<BsonDocument>
        {
            Backend = MongoEventStore.Create(mongoDb, mongoOptions),
            TypeMappings =
            [
                new EventTypeMapping(typeof(UserCreated))
            ],
            Serializer = new MongoBsonDocumentSerializer()
        };

        var eventStore = EventStoreFactory.Create(eventStoreOptions);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-mongo-bson-stream-{Guid.NewGuid()}";

        await eventStore.AppendToStreamAsync(streamId, [originalEvent]);

        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvents = await result.Events.ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<UserCreated>()
            .ShouldBe(originalEvent);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}