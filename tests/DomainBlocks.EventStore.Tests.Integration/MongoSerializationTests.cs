using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
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
        await Should_write_and_read_event(TestEvent, new BsonDocumentObjectSerde());
    }

    [Test]
    public async Task Should_write_and_read_event_as_proto_bytes()
    {
        var serde = new ProtobufBytesObjectSerde().AsBsonValueSerde();
        await Should_write_and_read_event(TestProtoEvent, serde);
    }

    [Test]
    public async Task Should_write_and_read_event_as_proto_json_string()
    {
        var serde = new ProtobufJsonObjectSerde().AsBsonValueSerde();
        await Should_write_and_read_event(TestProtoEvent, serde);
    }

    [Test]
    public async Task Should_write_and_read_event_as_json_bytes()
    {
        var serde = new JsonUtf8BytesObjectSerde().AsBsonValueSerde();
        await Should_write_and_read_event(TestEvent, serde);
    }

    [Test]
    public async Task Should_write_and_read_event_as_json_string()
    {
        var serde = new JsonObjectSerde().AsBsonValueSerde();
        await Should_write_and_read_event(TestEvent, serde);
    }

    private static async Task Should_write_and_read_event<TEvent>(
        TEvent @event,
        IObjectSerde<BsonValue> serde) where TEvent : class
    {
        using var mongoClient = new MongoClient(TestMongoConnectionStrings.Default);

        var options = new MongoEventStoreNodeOptions
        {
            DatabaseName = "domainblocks_tests"
        };

        await using var node = new MongoEventStoreNode(mongoClient, options);
        await node.StartAsync();

        var client = CreateEventStoreClient(node, serde);

        var streamId = $"test-{serde.GetType().Name}-{Guid.NewGuid()}";
        await client.AppendToStreamAsync(streamId, [@event]);
        var readEvents = await client.ReadStreamAsync(streamId).ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Event
            .ShouldBeOfType<TEvent>()
            .ShouldBe(@event);
    }

    private static IEventStoreClient<object> CreateEventStoreClient(
        MongoEventStoreNode node,
        IObjectSerde<BsonValue> serde)
    {
        var eventTypeMap = EventTypeMap.Create(builder => builder
            .MapType<UserCreated>()
            .MapType<Proto.UserCreated>(m => m.WithName("ProtoUserCreated")));

        var encoderOptions = new EventEncoderOptions<object, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap.Appends,
            EventSerializer = serde,
            MetadataSerializer = new BsonDocumentMetadataSerde()
        };

        var decoderOptions = new EventDecoderOptions<object, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap.Reads,
            EventDeserializer = serde,
            MetadataDeserializer = new BsonDocumentMetadataSerde()
        };

        var eventCode = new EventCodec<object, BsonValue, BsonValue>
        {
            Encoder = EventEncoder.Create(encoderOptions),
            Decoder = EventDecoder.Create(decoderOptions)
        };

        return node.CreateClient(eventCode);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}