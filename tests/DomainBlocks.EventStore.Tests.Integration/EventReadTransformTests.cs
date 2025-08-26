using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventReadTransformTests
{
    [Test]
    public async Task Should_transform_read_event()
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
            Serializer = new MongoBsonDocumentSerializer(),
            ReadTransforms =
            [
                new UserCreatedV2Upcaster()
            ]
        };

        var eventStore = new EventStore<BsonDocument>(eventStoreOptions);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-read-transform-{Guid.NewGuid()}";

        await eventStore.AppendToStreamAsync(streamId, [originalEvent]);

        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvents = await result.Events.ToArrayAsync();

        var readEvent = readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<UserCreatedV2>();

        readEvent.UserId.ShouldBe(originalEvent.UserId);
        readEvent.Name.ShouldBe(originalEvent.Name);
        readEvent.Surname.ShouldBeNull();
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }

    private record UserCreatedV2
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
        public string? Surname { get; init; }
    }

    private class UserCreatedV2Upcaster : EventReadTransform<UserCreated>
    {
        protected override IEnumerable<object> Apply(UserCreated @event, EventHeader header)
        {
            yield return new UserCreatedV2
            {
                UserId = @event.UserId,
                Name = @event.Name,
            };
        }
    }
}