using DomainBlocks.EventStore.MongoDB.Generic;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventStoreClientTests
{
    static EventStoreClientTests()
    {
        BsonClassMap.RegisterClassMap<LimitOrderEvent>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    [Test]
    public async Task Should_read_multiple_events_as_common_type()
    {
        var eventTypeMap = EventTypeMap.Create(builder => builder
            .ForAppends(appends => appends
                .MapType<LimitOrderSubmitted>()
                .MapType<LimitOrderAmended>()
                .MapType<LimitOrderFilled>())
            .ForReads(reads => reads
                .MapType<LimitOrderEvent>(mapping => mapping
                    .FromNames(
                        nameof(LimitOrderSubmitted),
                        nameof(LimitOrderAmended),
                        nameof(LimitOrderFilled)))));

        var codecOptions = new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerde = new BsonDocumentObjectSerde(),
            MetadataSerde = new BsonDocumentMetadataSerde()
        };

        var collectionOptions = EventStoreCollectionOptions.Default;

        var clientOptions = new MongoEventStoreClientOptions<object, EventDocument>
        {
            CollectionOptions = collectionOptions,
            EventDocumentSchema = EventDocumentSchema.Default,
            EventDocumentCodec = EventDocumentCodec.Create(EventCodec.Create(codecOptions))
        };

        using var mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var client = new MongoEventStoreClient<object, EventDocument>(mongoClient, clientOptions);

        var orderId = Guid.NewGuid();
        var streamId = $"order-{orderId}";

        var submitted = new LimitOrderSubmitted
        {
            OrderId = orderId,
            Quantity = 10,
            LimitPrice = 100,
            SubmittedAt = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero),
            SubmittedBy = "Bob"
        };

        var amended = new LimitOrderAmended
        {
            OrderId = orderId,
            Quantity = 11,
            LimitPrice = 101,
            AmendedAt = submitted.SubmittedAt.AddHours(1),
            AmendedBy = "Alice"
        };

        var filled = new LimitOrderFilled
        {
            OrderId = orderId,
            Quantity = 11,
            LimitPrice = 101,
            FillPrice = 99,
            FilledAt = amended.AmendedAt.AddHours(1)
        };

        await client.AppendToStreamAsync(streamId, [submitted, amended, filled]);

        var orderEvents = await client
            .ReadStreamAsync(streamId)
            .Unwrap()
            .OfType<LimitOrderEvent>()
            .ToArrayAsync();

        orderEvents.Length.ShouldBe(3);

        orderEvents[0].OrderId.ShouldBe(orderId);
        orderEvents[0].Quantity.ShouldBe(10);
        orderEvents[0].LimitPrice.ShouldBe(100);

        orderEvents[1].OrderId.ShouldBe(orderId);
        orderEvents[1].Quantity.ShouldBe(11);
        orderEvents[1].LimitPrice.ShouldBe(101);

        orderEvents[2].OrderId.ShouldBe(orderId);
        orderEvents[2].Quantity.ShouldBe(11);
        orderEvents[2].LimitPrice.ShouldBe(101);
    }

    // ReSharper disable UnusedAutoPropertyAccessor.Global
    public record LimitOrderSubmitted
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
        public required DateTimeOffset SubmittedAt { get; init; }
        public required string SubmittedBy { get; init; }
    }

    public record LimitOrderAmended
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
        public required DateTimeOffset AmendedAt { get; init; }
        public required string AmendedBy { get; init; }
    }

    public record LimitOrderFilled
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
        public required decimal FillPrice { get; init; }
        public required DateTimeOffset FilledAt { get; init; }
    }

    // ReSharper disable once ClassNeverInstantiated.Global
    public record LimitOrderEvent
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
    }
    // ReSharper restore UnusedAutoPropertyAccessor.Global
}