using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Generic;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class ReadEventTransformTests
{
    [Test]
    public async Task Should_transform_read_event()
    {
        var shipmentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var dispatchedAt = new DateTime(2025, 08, 25, 14, 30, 0, DateTimeKind.Utc);

        var legacyEvent = new ShipmentDispatched(
            shipmentId,
            dispatchedAt,
            new List<PackageInfo>
            {
                new("TRACK-001", 2.5, "Lisbon, PT"),
                new("TRACK-002", 1.2, "Porto, PT"),
                new("TRACK-003", 5.0, "Madrid, ES")
            });

        var expectedEvents = new object[]
        {
            new ShipmentDispatchedV2(
                shipmentId,
                dispatchedAt),

            new PackageShipped(
                shipmentId,
                TrackingNumber: "TRACK-001",
                WeightKg: 2.5,
                Destination: "Lisbon, PT"),

            new PackageShipped(
                shipmentId,
                TrackingNumber: "TRACK-002",
                WeightKg: 1.2,
                Destination: "Porto, PT"),

            new PackageShipped(
                shipmentId,
                TrackingNumber: "TRACK-003",
                WeightKg: 5.0,
                Destination: "Madrid, ES")
        };

        var codecOptions = new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = EventTypeMap.Create(x => x.MapType<ShipmentDispatched>()),
            EventSerde = new BsonDocumentObjectSerde(),
            MetadataSerde = new BsonDocumentMetadataSerde()
        };

        var options = new MongoEventStoreClientOptions<object, EventDocument>
        {
            CollectionOptions = EventStoreCollectionOptions.Default,
            EventDocumentSchema = EventDocumentSchema.Default,
            EventDocumentCodec = EventDocumentCodec.Create(EventCodec.Create(codecOptions))
        };

        using var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var client = new MongoEventStoreClient<object, EventDocument>(mongoClient, options);

        var streamId = $"test-read-transform-{Guid.NewGuid()}";
        await client.AppendToStreamAsync(streamId, [legacyEvent]);

        var readEvents = await client
            .ReadStreamAsync(streamId)
            .Transform([new ShipmentDispatchedTransform()])
            .Unwrap()
            .ToArrayAsync();

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

    private class ShipmentDispatchedTransform : ReadEventTransform<object, ShipmentDispatched>
    {
        protected override IEnumerable<object> Apply(
            ShipmentDispatched @event,
            IReadOnlyDictionary<string, string> metadata,
            ReadEventContext context)
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