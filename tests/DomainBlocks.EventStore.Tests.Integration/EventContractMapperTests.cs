using DomainBlocks.EventStore.KurrentDB;
using DomainBlocks.EventStore.TypeMapping;
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

        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<Proto.UserCreated>()
            .Build();

        var codecOptions = new EventCodecOptions<object, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = eventTypeMap,
            EventSerializer = new SystemTextJsonBytesSerializer(),
            MetadataSerializer = new SystemTextJsonBytesMetadataSerializer(),
            ContractMappers = [new UserCreatedProtoMapper()]
        };

        var codec = EventCodec.Create(codecOptions);
        var client = new KurrentDBEventStoreClient<object>(kurrentClient, codec);

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
            .Event
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