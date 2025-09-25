using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class MongoSerializationTests : MongoEventStoreTestFixture<BsonValue>
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
    public async Task Should_write_and_read_event_with_bson_document_payload()
    {
        await Should_write_and_read_event(TestEvent, new BsonDocumentSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_with_bson_bytes_payload()
    {
        await Should_write_and_read_event(TestEvent, new BsonBytesSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_with_proto_bytes_payload()
    {
        var serializer = new ProtobufBytesSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestProtoEvent, serializer);
    }

    [Test]
    public async Task Should_write_and_read_event_with_proto_json_string_payload()
    {
        var serializer = new ProtobufJsonStringSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestProtoEvent, serializer);
    }

    [Test]
    public async Task Should_write_and_read_event_with_json_bytes_payload()
    {
        var serializer = new SystemTextJsonBytesSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestEvent, serializer);
    }

    [Test]
    public async Task Should_write_and_read_event_with_json_string_payload()
    {
        var serializer = new SystemTextJsonStringSerializer().AsBsonValueSerializer();
        await Should_write_and_read_event(TestEvent, serializer);
    }

    private async Task Should_write_and_read_event<TEvent>(
        TEvent @event,
        IPayloadSerializer<BsonValue> serializer) where TEvent : notnull
    {
        var eventStore = CreateEventStore(serializer);
        var streamId = $"test-{serializer.GetType().Name}-{Guid.NewGuid()}";
        await eventStore.AppendToStreamAsync(streamId, [@event]);
        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvents = await result.Events.ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<TEvent>()
            .ShouldBe(@event);
    }

    private EventStore<BsonValue> CreateEventStore(IPayloadSerializer<BsonValue> serializer)
    {
        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<UserCreated>()
            .MapType<Proto.UserCreated>("ProtoUserCreated")
            .Build();

        var eventStoreOptions = new EventStoreOptions<BsonValue>
        {
            Backend = MongoEventStore,
            TypeMap = eventTypeMap,
            Serializer = serializer
        };

        var eventStore = new EventStore<BsonValue>(eventStoreOptions);

        return eventStore;
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}