using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class MongoSerializationTests : MongoEventStoreTestFixture
{
    private static readonly UserCreated TestEvent = new()
    {
        UserId = "user-123",
        Name = "Alice"
    };

    private static readonly Proto.UserCreated TestProtoEvent = new()
    {
        UserId = "user-123",
        Name = "Alice"
    };

    [Test]
    public async Task Should_write_and_read_event_as_bson_document()
    {
        await Should_write_and_read_event(TestEvent, new BsonDocumentSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_as_bson_bytes()
    {
        await Should_write_and_read_event(TestEvent, new BsonBytesSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_as_proto_bytes()
    {
        var serializer = new ProtobufBytesSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestProtoEvent, serializer);
    }

    [Test]
    public async Task Should_write_and_read_event_as_proto_json_string()
    {
        var serializer = new ProtobufJsonStringSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestProtoEvent, serializer);
    }

    [Test]
    public async Task Should_write_and_read_event_as_json_bytes()
    {
        var serializer = new SystemTextJsonBytesSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestEvent, serializer);
    }

    [Test]
    public async Task Should_write_and_read_event_as_json_string()
    {
        var serializer = new SystemTextJsonStringSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestEvent, serializer);
    }

    private async Task Should_write_and_read_event<TEvent>(
        TEvent @event,
        IObjectSerializer<BsonValue> serializer) where TEvent : class
    {
        var client = CreateEventStore(serializer);
        var streamId = $"test-{serializer.GetType().Name}-{Guid.NewGuid()}";
        await client.AppendToStreamAsync(streamId, [@event]);
        var readEvents = await client.ReadStreamAsync(streamId).ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Value
            .ShouldBeOfType<TEvent>()
            .ShouldBe(@event);
    }

    private static EventStoreClient<object, BsonValue, BsonValue> CreateEventStore(
        IObjectSerializer<BsonValue> serializer)
    {
        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<UserCreated>()
            .MapType<Proto.UserCreated>("ProtoUserCreated")
            .Build();

        var clientOptions = new EventStoreClientOptions<object, BsonValue, BsonValue>
        {
            AdapterFactory = async ct => await MongoEventStoreAdapterFactory.CreateAsync(ct),
            TypeMap = eventTypeMap,
            EventSerializer = serializer,
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        };

        return new EventStoreClient<object, BsonValue, BsonValue>(clientOptions);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}