using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBocks.Testing.Integration.MongoDB;
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
    public async Task Should_write_and_read_event_with_bson_document_payload()
    {
        await Should_write_and_read_event(TestEvent, new MongoBsonDocumentSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_with_bson_bytes_payload()
    {
        await Should_write_and_read_event<UserCreated, byte[]>(TestEvent, new MongoBsonBytesSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_with_proto_bytes_payload()
    {
        await Should_write_and_read_event<Proto.UserCreated, byte[]>(
            TestProtoEvent,
            new GoogleProtobufBytesSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_with_proto_json_string_payload()
    {
        await Should_write_and_read_event(TestProtoEvent, new GoogleProtobufJsonStringSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_with_json_bytes_payload()
    {
        await Should_write_and_read_event<UserCreated, byte[]>(TestEvent, new SystemTextJsonBytesSerializer());
    }

    [Test]
    public async Task Should_write_and_read_event_with_json_string_payload()
    {
        await Should_write_and_read_event(TestEvent, new SystemTextJsonStringSerializer());
    }

    private async Task Should_write_and_read_event<TEvent, TPayload>(
        TEvent @event,
        IPayloadSerializer<TPayload> serializer) where TEvent : notnull where TPayload : notnull
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

    private EventStore<TPayload> CreateEventStore<TPayload>(IPayloadSerializer<TPayload> serializer)
        where TPayload : notnull
    {
        var eventStoreBackend = MongoEventStore.Create<EventDocument, TPayload>(MongoDatabase, MongoEventStoreOptions);

        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<UserCreated>()
            .MapType<Proto.UserCreated>("ProtoUserCreated")
            .Build();

        var eventStoreOptions = new EventStoreOptions<TPayload>
        {
            Backend = eventStoreBackend,
            TypeMap = eventTypeMap,
            Serializer = serializer
        };

        var eventStore = new EventStore<TPayload>(eventStoreOptions);

        return eventStore;
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}