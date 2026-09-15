using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Events.Proto;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// The BSON value adapters over string serializers, which store the event as a string field. The BSON document, JSON bytes
/// and Protobuf bytes serializers are covered by the shared event format tests.
/// </summary>
[TestFixture]
public class MongoBsonValueSerializerTests
{
    private readonly MongoEventStoreTestHarness _harness = new();

    [OneTimeSetUp]
    public Task InitializeDatabaseAsync() => _harness.InitializeAsync(TestStoreName.For(this));

    [OneTimeTearDown]
    public Task DropDatabaseAsync() => _harness.DropAsync();

    [Test]
    public async Task AppendAsync_JsonStringSerializer_RoundTripsEvent()
    {
        var @event = new TestEvent { Value = "test-123" };

        await ShouldRoundTripAsync(@event, new JsonObjectSerializer().AsBsonValueSerializer());
    }

    [Test]
    public async Task AppendAsync_ProtobufJsonStringSerializer_RoundTripsEvent()
    {
        var @event = new ProtoTestEvent { Value = "test-123" };

        await ShouldRoundTripAsync(@event, new ProtobufJsonObjectSerializer().AsBsonValueSerializer());
    }

    private async Task ShouldRoundTripAsync<TEvent>(TEvent @event, IObjectSerializer<BsonValue> serializer)
        where TEvent : class
    {
        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<TestEvent>(),
            EventTypeMapping.ReadWrite<ProtoTestEvent>(nameof(ProtoTestEvent)));

        var eventCodec = EventCodec.Create(new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerializer = serializer,
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        });

        await using var eventStore = MongoEventStore.Create(
            MongoTestEnvironment.MongoClient,
            eventCodec,
            _harness.Options);

        var streamId = $"test-{serializer.GetType().Name}-{Guid.NewGuid():N}";

        await eventStore.AppendAsync(streamId, [@event]);

        var readEvents = await eventStore.ReadStream(streamId).ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<TEvent>()
            .ShouldBe(@event);
    }
}