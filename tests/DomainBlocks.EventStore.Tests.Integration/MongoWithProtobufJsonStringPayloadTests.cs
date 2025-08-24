using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.Tests.Integration.Generated;
using DomainBlocks.Serialization.Google.Protobuf;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class MongoWithProtobufJsonStringPayloadTests
{
    [Test]
    public async Task Should_write_and_read_event()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDb = client.GetDatabase("test");
        var mongoOptions = MongoEventStoreOptions.CreateDefault<string>();
        await MongoEventStore.EnsureIndexesAsync(mongoDb, mongoOptions);

        var eventStoreOptions = new EventStoreOptions<string>
        {
            Backend = MongoEventStore.Create(mongoDb, mongoOptions),
            TypeMappings =
            [
                new EventTypeMapping(typeof(UserCreated))
            ],
            Serializer = new GoogleProtobufJsonStringSerializer()
        };

        var eventStore = EventStoreFactory.Create(eventStoreOptions);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-bson-stream-{Guid.NewGuid()}";

        await eventStore.AppendToStreamAsync(streamId, [originalEvent]);

        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvents = await result.Events.ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<UserCreated>()
            .ShouldBe(originalEvent);
    }
}