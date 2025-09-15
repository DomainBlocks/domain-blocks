using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class MongoSerializationTests
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

    private static async Task Should_write_and_read_event<TEvent, TPayload>(
        TEvent @event,
        IPayloadSerializer<TPayload> serializer)
        where TEvent : notnull
    {
        var eventStore = await CreateEventStore(serializer);
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

    private static async Task<EventStore<TPayload>> CreateEventStore<TPayload>(IPayloadSerializer<TPayload> serializer)
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDb = client.GetDatabase("test");
        var mongoOptions = MongoEventStoreOptions.CreateDefault<TPayload>();
        await MongoEventStore.EnsureIndexesAsync(mongoDb, mongoOptions);

        var eventStoreOptions = new EventStoreOptions<TPayload>
        {
            Backend = MongoEventStore.Create(mongoDb, mongoOptions),
            TypeMappings =
            [
                new EventTypeMapping(typeof(UserCreated)),
                new EventTypeMapping(typeof(Proto.UserCreated), "ProtoUserCreated")
            ],
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