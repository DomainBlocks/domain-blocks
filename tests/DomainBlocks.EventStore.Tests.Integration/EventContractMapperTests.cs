using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventContractMapperTests : MongoEventStoreTestFixture
{
    [Test]
    public async Task Should_map_to_and_from_contract()
    {
        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<Proto.UserCreated>()
            .Build();

        var clientOptions = new EventStoreClientOptions<object, BsonValue, BsonValue>
        {
            AdapterFactory = async ct => await MongoEventStoreAdapterFactory.CreateAsync(ct),
            TypeMap = eventTypeMap,
            EventSerializer = new ProtobufBytesSerializer().AsBsonValueSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer(),
            ContractMappers =
            [
                new UserCreatedProtoMapper()
            ]
        };

        var client = new EventStoreClient<object, BsonValue, BsonValue>(clientOptions);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-contract-mapper-{Guid.NewGuid()}";

        await client.AppendToStreamAsync(streamId, [originalEvent]);

        var readEvents = await client.ReadStreamAsync(streamId).ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Value
            .ShouldBeOfType<UserCreated>()
            .ShouldBe(originalEvent);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }

    private class UserCreatedProtoMapper : EventContractMapper<object, UserCreated, Proto.UserCreated>
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