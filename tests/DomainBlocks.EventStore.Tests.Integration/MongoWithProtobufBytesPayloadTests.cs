using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.Google.Protobuf;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class MongoWithProtobufBytesPayloadTests
{
    [Test]
    public async Task Should_write_and_read_event()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDb = client.GetDatabase("test");
        var mongoOptions = MongoEventStoreOptions.CreateDefault<byte[]>();
        await MongoEventStore.EnsureIndexesAsync(mongoDb, mongoOptions);

        var eventStoreOptions = new EventStoreOptions<byte[]>
        {
            Backend = MongoEventStore.Create(mongoDb, mongoOptions),
            TypeMappings =
            [
                new EventTypeMapping(typeof(Proto.UserCreated))
            ],
            Serializer = new GoogleProtobufBytesSerializer()
        };

        var eventStore = EventStoreFactory.Create(eventStoreOptions);

        var originalEvent = new Proto.UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-mongo-proto-bytes-stream-{Guid.NewGuid()}";

        await eventStore.AppendToStreamAsync(streamId, [originalEvent]);

        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvents = await result.Events.ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<Proto.UserCreated>()
            .ShouldBe(originalEvent);
    }
}