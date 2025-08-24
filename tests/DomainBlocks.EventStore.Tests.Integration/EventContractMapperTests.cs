using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.Google.Protobuf;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventContractMapperTests
{
    [Test]
    public async Task Should_map_to_and_from_contract()
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
            Serializer = new GoogleProtobufBytesSerializer(),
            ContractMappers =
            [
                new UserCreatedProtoMapper()
            ]
        };

        var eventStore = EventStoreFactory.Create(eventStoreOptions);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-contract-mapper-stream-{Guid.NewGuid()}";

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

    private class UserCreatedProtoMapper : EventContractMapper<UserCreated, Proto.UserCreated>
    {
        protected override Proto.UserCreated ToContract(UserCreated @event)
        {
            return new Proto.UserCreated
            {
                UserId = @event.UserId,
                Name = @event.Name
            };
        }

        protected override UserCreated FromContract(Proto.UserCreated contract)
        {
            return new UserCreated
            {
                UserId = contract.UserId,
                Name = contract.Name
            };
        }
    }
}