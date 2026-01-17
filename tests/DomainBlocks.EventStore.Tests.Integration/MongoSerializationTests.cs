using DomainBlocks.EventStore.MongoDB.Generic;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
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
    public async Task Should_write_and_read_event_as_bson_document()
    {
        await Should_write_and_read_event(TestEvent, new BsonDocumentSerializer());
    }

    // [Test]
    // public async Task Should_write_and_read_event_as_bson_bytes()
    // {
    //     await Should_write_and_read_event(TestEvent, new BsonBytesSerializer());
    // }

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

    private static async Task Should_write_and_read_event<TEvent>(
        TEvent @event,
        IObjectSerializer<BsonValue> serializer) where TEvent : class
    {
        using var mongoClient = new MongoClient(MongoConnectionStrings.Default);

        var client = CreateEventStoreClient(mongoClient, serializer);

        var streamId = $"test-{serializer.GetType().Name}-{Guid.NewGuid()}";
        await client.AppendToStreamAsync(streamId, [@event]);
        var readEvents = await client.ReadStreamAsync(streamId).ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Event
            .ShouldBeOfType<TEvent>()
            .ShouldBe(@event);
    }

    private static MongoEventStoreClient<object, EventDocument> CreateEventStoreClient(
        MongoClient mongoClient,
        IObjectSerializer<BsonValue> serializer)
    {
        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<UserCreated>()
            .MapType<Proto.UserCreated>("ProtoUserCreated")
            .Build();

        var codecOptions = new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerializer = serializer,
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        };

        var options = new MongoEventStoreClientOptions<object, EventDocument>
        {
            CollectionOptions = EventStoreCollectionOptions.Default,
            EventDocumentSchema = EventDocumentSchema.Default,
            EventDocumentCodec = EventDocumentCodec.Create(EventCodec.Create(codecOptions))
        };

        return new MongoEventStoreClient<object, EventDocument>(mongoClient, options);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}