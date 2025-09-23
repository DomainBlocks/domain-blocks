using DomainBlocks.Serialization.Google.Protobuf;
using DomainBocks.Testing.Integration.MongoDB;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventContractMapperTests : MongoEventStoreTestFixture<byte[]>
{
    [Test]
    public async Task Should_map_to_and_from_contract()
    {
        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<Proto.UserCreated>()
            .Build();

        var eventStoreOptions = new EventStoreOptions<byte[]>
        {
            Backend = MongoEventStore,
            TypeMap = eventTypeMap,
            Serializer = new ProtobufBytesSerializer(),
            ContractMappers =
            [
                new UserCreatedProtoMapper()
            ]
        };

        var eventStore = new EventStore<byte[]>(eventStoreOptions);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-contract-mapper-{Guid.NewGuid()}";

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