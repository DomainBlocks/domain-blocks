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
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<EventDocument<string>>("events");

        var mongoOptions = new MongoEventStoreOptions<EventDocument<string>, string>
        {
            DocumentMapper = new EventDocumentMapper<string>(),
            StreamIdSelector = doc => doc.StreamId,
            StreamVersionSelector = doc => doc.StreamVersion
        };

        var eventStoreOptions = new EventStoreOptions<string>
        {
            Backend = MongoEventStore.Create(collection, mongoOptions),
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