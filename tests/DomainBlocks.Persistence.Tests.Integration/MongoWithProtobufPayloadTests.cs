using DomainBlocks.Persistence.Events;
using DomainBlocks.Persistence.Mongo.Events;
using DomainBlocks.Persistence.Tests.Integration.Generated;
using DomainBlocks.Serialization.Events;
using DomainBlocks.Serialization.GoogleProtobuf;
using Google.Protobuf;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Persistence.Tests.Integration;

public class MongoWithProtobufPayloadTests
{
    [Test]
    public async Task Should_write_and_read_event()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<BsonDocument>("events");

        var eventDataStore = new MongoEventDataStore<ReadOnlyMemory<byte>>(
            collection,
            x => x.ToArray(),
            x => x.AsByteArray);

        EventTypeMapping[] mappings =
        [
            new(typeof(UserCreated))
        ];

        var serializer = new GoogleProtobufSerializer();
        var eventSerializer = new EventSerializer<ReadOnlyMemory<byte>>(mappings, serializer);
        var eventStore = new EventStore<IMessage, ReadOnlyMemory<byte>>(eventDataStore, eventSerializer);

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