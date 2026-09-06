using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;
using ProtoUserCreated = DomainBlocks.Testing.Integration.Proto.UserCreated;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public class MongoSerializationTests
{
    private static readonly UserCreated TestEvent = new()
    {
        UserId = "user-123",
        Name = "Alice"
    };

    private static readonly ProtoUserCreated TestProtoEvent = new()
    {
        UserId = "user-123",
        Name = "Alice"
    };

    private MongoEventStoreOptions _options = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _options = new MongoEventStoreOptions { DatabaseName = "dbx_es_serialization_tests" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.MongoClient, _options);
    }

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

    private async Task Should_write_and_read_event<TEvent>(TEvent @event, IObjectSerde<BsonValue> serde)
        where TEvent : class
    {
        await using var eventStore = CreateEventStore(SetUpFixture.MongoClient, serde, _options);

        var streamId = $"test-{serde.GetType().Name}-{Guid.NewGuid()}";
        await eventStore.AppendAsync(streamId, [@event]);
        var readEvents = await eventStore.ReadStream(streamId).ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<TEvent>()
            .ShouldBe(@event);
    }

    private static MongoEventStore<object> CreateEventStore(
        IMongoClient mongoClient,
        IObjectSerde<BsonValue> serde,
        MongoEventStoreOptions options)
    {
        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<UserCreated>(),
            EventTypeMapping.ReadWrite<ProtoUserCreated>(nameof(ProtoUserCreated)));

        var codecOptions = new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerde = serde,
            MetadataSerde = new BsonDocumentMetadataSerde()
        };

        var eventCodec = EventCodec.Create(codecOptions);

        return MongoEventStore.Create(mongoClient, eventCodec, options);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }
}