using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;
using ProtoUserCreated = DomainBlocks.Testing.Integration.Proto.UserCreated;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreEventRepresentationTests<TStreamPos, TLogPos> :
    EventStoreTestBase<object, string, TStreamPos, TLogPos>
    where TStreamPos : notnull
    where TLogPos : notnull
{
    [Test]
    public async Task Should_read_multiple_events_as_common_type()
    {
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

        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.WriteOnly<LimitOrderSubmitted>(),
            EventTypeMapping.WriteOnly<LimitOrderAmended>(),
            EventTypeMapping.WriteOnly<LimitOrderFilled>(),
            EventTypeMapping.ReadOnly<LimitOrderEvent>(
                nameof(LimitOrderSubmitted),
                nameof(LimitOrderAmended),
                nameof(LimitOrderFilled)));

        var eventStore = CreateEventStore(eventTypeMap);
        LimitOrderEvent[] orderEvents;

        try
        {
            await eventStore.AppendAsync(streamId, [submitted, amended, filled]);

            orderEvents = await eventStore
                .ReadStream(streamId)
                .Select(x => x.Payload)
                .OfType<LimitOrderEvent>()
                .ToArrayAsync();
        }
        finally
        {
            if (eventStore is IAsyncDisposable d)
                await d.DisposeAsync();
        }

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

    [Test]
    public async Task Should_map_to_and_from_contract()
    {
        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<ProtoUserCreated>());
        var streamId = $"test-contract-mapper-{Guid.NewGuid()}";

        var originalEvent = new UserCreated
        {
            UserId = "user-123",
            Name = "Alice"
        };

        var eventStore = CreateEventStore(
            eventTypeMap,
            eventFormat: EventFormat.Protobuf,
            contractMappers: [new UserCreatedProtoMapper()]);

        object[] readEvents;

        try
        {
            await eventStore.AppendAsync(streamId, [originalEvent]);
            readEvents = await eventStore.ReadStream(streamId).Select(x => x.Payload).ToArrayAsync();
        }
        finally
        {
            if (eventStore is IAsyncDisposable d)
                await d.DisposeAsync();
        }

        readEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<UserCreated>()
            .ShouldBe(originalEvent);
    }

    [Test]
    public async Task Should_transform_read_event()
    {
        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<ShipmentDispatched>());
        var streamId = $"test-read-transform-{Guid.NewGuid()}";
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

        var eventStore = CreateEventStore(eventTypeMap);
        object[] readEvents;

        try
        {
            await eventStore.AppendAsync(streamId, [legacyEvent]);

            readEvents = await eventStore
                .ReadStream(streamId)
                .Transform([new ShipmentDispatchedTransform()])
                .Select(x => x.Payload)
                .ToArrayAsync();
        }
        finally
        {
            if (eventStore is IAsyncDisposable d)
                await d.DisposeAsync();
        }

        readEvents.ShouldBe(expectedEvents);
    }

    private record LimitOrderSubmitted
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
        public required DateTimeOffset SubmittedAt { get; init; }
        public required string SubmittedBy { get; init; }
    }

    private record LimitOrderAmended
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
        public required DateTimeOffset AmendedAt { get; init; }
        public required string AmendedBy { get; init; }
    }

    private record LimitOrderFilled
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
        public required decimal FillPrice { get; init; }
        public required DateTimeOffset FilledAt { get; init; }
    }

    // Protected as Mongo test needs to ignore extra elements.
    protected record LimitOrderEvent
    {
        public required Guid OrderId { get; init; }
        public required int Quantity { get; init; }
        public required decimal LimitPrice { get; init; }
    }

    private record UserCreated
    {
        public required string UserId { get; init; }
        public required string Name { get; init; }
    }

    private class UserCreatedProtoMapper : EventContractMapper<object, UserCreated, ProtoUserCreated>
    {
        protected override ProtoUserCreated ToContract(UserCreated @event)
        {
            return new ProtoUserCreated
            {
                UserId = @event.UserId,
                Name = @event.Name
            };
        }

        protected override UserCreated FromContract(ProtoUserCreated contract)
        {
            return new UserCreated
            {
                UserId = contract.UserId,
                Name = contract.Name
            };
        }
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

    private class ShipmentDispatchedTransform :
        ReadEventTransform<object, ShipmentDispatched, string, TStreamPos, TLogPos>
    {
        protected override IEnumerable<object> Apply(
            ShipmentDispatched @event,
            ReadEventContext<string, TStreamPos, TLogPos> context)
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