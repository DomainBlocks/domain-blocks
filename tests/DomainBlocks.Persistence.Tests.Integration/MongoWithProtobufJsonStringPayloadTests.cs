using DomainBlocks.Persistence.Events;
using DomainBlocks.Persistence.MongoDB.Events;
using DomainBlocks.Persistence.Tests.Integration.Generated;
using DomainBlocks.Serialization.Events;
using DomainBlocks.Serialization.Google.Protobuf;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Persistence.Tests.Integration;

public class MongoWithProtobufJsonStringPayloadTests
{
    [Test]
    public async Task Should_write_and_read_event()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<BsonDocument>("events");

        var eventDataStore = new MongoEventDataStore<string>(
            collection,
            x => new BsonString(x),
            x => x.AsString);

        EventTypeMapping[] mappings =
        [
            new(typeof(UserCreated))
        ];

        var serializer = new GoogleProtobufJsonStringSerializer();
        var eventSerializer = new EventSerializer<string>(mappings, serializer);
        var eventStore = new EventStore<string>(eventDataStore, eventSerializer);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-proto-stream-{Guid.NewGuid()}";

        await eventStore.AppendToStreamAsync(streamId, [originalEvent]);

        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvent = await result.Events.ToArrayAsync();

        readEvent
            .ShouldHaveSingleItem()
            .ShouldBeOfType<UserCreated>()
            .ShouldBe(originalEvent);
    }
}