using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Google.Protobuf;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;
using ProtoUserCreated = DomainBlocks.Testing.Integration.Proto.UserCreated;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// The BSON value adapters over string serdes, which store the event as a string field. The BSON document, JSON bytes
/// and Protobuf bytes serdes are covered by the shared event format tests.
/// </summary>
[TestFixture]
public class MongoBsonValueSerdeTests
{
    private readonly MongoEventStoreHarness _harness = new();

    [OneTimeSetUp]
    public Task InitializeDatabaseAsync() => _harness.InitializeAsync(TestStoreName.For(this));

    [OneTimeTearDown]
    public Task DropDatabaseAsync() => _harness.DropAsync();

    [Test]
    public async Task AppendAsync_JsonStringSerde_RoundTripsEvent()
    {
        var @event = new UserCreated { UserId = "user-123", Name = "Alice" };

        await ShouldRoundTripAsync(@event, new JsonObjectSerde().AsBsonValueSerde());
    }

    [Test]
    public async Task AppendAsync_ProtobufJsonStringSerde_RoundTripsEvent()
    {
        var @event = new ProtoUserCreated { UserId = "user-123", Name = "Alice" };

        await ShouldRoundTripAsync(@event, new ProtobufJsonObjectSerde().AsBsonValueSerde());
    }

    private async Task ShouldRoundTripAsync<TEvent>(TEvent @event, IObjectSerde<BsonValue> serde)
        where TEvent : class
    {
        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<UserCreated>(),
            EventTypeMapping.ReadWrite<ProtoUserCreated>(nameof(ProtoUserCreated)));

        var eventCodec = EventCodec.Create(new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerde = serde,
            MetadataSerde = new BsonDocumentMetadataSerde()
        });

        await using var eventStore = MongoEventStore.Create(
            MongoTestEnvironment.MongoClient,
            eventCodec,
            _harness.Options);

        var streamId = $"test-{serde.GetType().Name}-{Guid.NewGuid():N}";

        await eventStore.AppendAsync(streamId, [@event]);

        var readEvents = await eventStore.ReadStream(streamId).ToArrayAsync();

        readEvents
            .ShouldHaveSingleItem()
            .Payload
            .ShouldBeOfType<TEvent>()
            .ShouldBe(@event);
    }

    private record UserCreated
    {
        public required string UserId { get; init; }

        public required string Name { get; init; }
    }
}