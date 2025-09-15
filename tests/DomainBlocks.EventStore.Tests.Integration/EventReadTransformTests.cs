using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventReadTransformTests
{
    static EventReadTransformTests()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }

    [Test]
    public async Task Should_transform_read_event()
    {
        var shipmentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var dispatchedAt = new DateTime(2025, 08, 25, 14, 30, 0, DateTimeKind.Utc);

        var legacyEvent = new ShipmentDispatched(
            ShipmentId: shipmentId,
            DispatchedAt: dispatchedAt,
            Packages: new List<PackageInfo>
            {
                new("TRACK-001", 2.5, "Lisbon, PT"),
                new("TRACK-002", 1.2, "Porto, PT"),
                new("TRACK-003", 5.0, "Madrid, ES")
            });

        var expectedEvents = new object[]
        {
            new ShipmentDispatchedV2(
                ShipmentId: shipmentId,
                DispatchedAt: dispatchedAt),

            new PackageShipped(
                ShipmentId: shipmentId,
                TrackingNumber: "TRACK-001",
                WeightKg: 2.5,
                Destination: "Lisbon, PT"),

            new PackageShipped(
                ShipmentId: shipmentId,
                TrackingNumber: "TRACK-002",
                WeightKg: 1.2,
                Destination: "Porto, PT"),

            new PackageShipped(
                ShipmentId: shipmentId,
                TrackingNumber: "TRACK-003",
                WeightKg: 5.0,
                Destination: "Madrid, ES")
        };

        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDb = client.GetDatabase("test");
        var mongoOptions = MongoEventStoreOptions.CreateDefault();
        await MongoEventStore.EnsureIndexesAsync(mongoDb, mongoOptions);

        var eventStoreOptions = new EventStoreOptions<BsonDocument>
        {
            Backend = MongoEventStore.Create(mongoDb, mongoOptions),
            TypeMappings =
            [
                new EventTypeMapping(typeof(ShipmentDispatched))
            ],
            Serializer = new MongoBsonDocumentSerializer(),
            ReadTransforms =
            [
                new ShipmentDispatchedTransform()
            ]
        };

        var eventStore = new EventStore<BsonDocument>(eventStoreOptions);
        var streamId = $"test-read-transform-{Guid.NewGuid()}";
        await eventStore.AppendToStreamAsync(streamId, [legacyEvent]);
        var result = await eventStore.ReadStreamAsync(streamId);
        var readEvents = await result.Events.Select(x => x.Payload).ToArrayAsync();

        readEvents.ShouldBe(expectedEvents);
    }

    private record ShipmentDispatched(
        Guid ShipmentId,
        DateTime DispatchedAt,
        IReadOnlyList<PackageInfo> Packages);

    private record PackageInfo(string TrackingNumber, double WeightKg, string Destination);

    private record ShipmentDispatchedV2(
        Guid ShipmentId,
        DateTime DispatchedAt);

    private record PackageShipped(
        Guid ShipmentId,
        string TrackingNumber,
        double WeightKg,
        string Destination);

    private class ShipmentDispatchedTransform : EventReadTransform<ShipmentDispatched>
    {
        protected override IEnumerable<object> Apply(ShipmentDispatched @event, EventHeader header)
        {
            yield return new ShipmentDispatchedV2(
                @event.ShipmentId,
                @event.DispatchedAt);

            foreach (var pkg in @event.Packages)
            {
                yield return new PackageShipped(
                    @event.ShipmentId,
                    pkg.TrackingNumber,
                    pkg.WeightKg,
                    pkg.Destination);
            }
        }
    }
}