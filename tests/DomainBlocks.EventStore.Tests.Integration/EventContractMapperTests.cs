using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.KurrentDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventContractMapperTests
{
    [Test]
    public async Task Should_map_to_and_from_contract()
    {
        const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
        await using var kurrentClient = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));

        var eventTypeMap = EventTypeMap.Create(x => x.MapType<Proto.UserCreated>());

        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = eventTypeMap,
            EventSerde = new ProtobufBytesObjectSerde(),
            MetadataSerde = new JsonUtf8BytesMetadataSerde(),
            ContractMappers = [new UserCreatedProtoMapper()]
        };

        var codec = EventCodec.Create(codecOptions);
        var client = new KurrentDBEventStoreClient<IDomainEvent>(kurrentClient, codec.Encoder, codec.Decoder);

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var streamId = $"test-contract-mapper-{Guid.NewGuid()}";

        await client.AppendToStreamAsync(streamId, [originalEvent]);

        var readEvents = await client.ReadStreamAsync(streamId).Unwrap().ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<UserCreated>()
            .ShouldBe(originalEvent);
    }

    private interface IDomainEvent;

    private record UserCreated : IDomainEvent
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }

    private class UserCreatedProtoMapper : EventContractMapper<IDomainEvent, UserCreated, Proto.UserCreated>
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