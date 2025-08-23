using DomainBlocks.Persistence.Events;
using DomainBlocks.Persistence.MongoDB.Events;
using DomainBlocks.Persistence.Tests.Integration.Generated;
using DomainBlocks.Serialization.Events;
using DomainBlocks.Serialization.Google.Protobuf;
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
        var collection = database.GetCollection<EventDocument<string>>("events");

        var options = new MongoEventStoreOptions<EventDocument<string>, string>
        {
            DocumentMapper = new EventDocumentMapper<string>(),
            StreamIdSelector = doc => doc.StreamId,
            StreamVersionSelector = doc => doc.StreamVersion
        };

        var mongoEventStore = MongoEventStore.Create(collection, options);

        EventTypeMapping[] eventTypeMappings =
        [
            new(typeof(UserCreated))
        ];

        var serializer = new GoogleProtobufJsonStringSerializer();
        var eventStore = EventStore.Create(mongoEventStore, eventTypeMappings, serializer);

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