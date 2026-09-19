using DomainBlocks.EventStore;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Events.Proto;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

public abstract class EventStoreEventRepresentationTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    [Test]
    public async Task ReadStream_TypesMappedToCommonReadType_ReturnsCommonType()
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
            await eventStore.DisposeAsync();
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
    public async Task AppendAsync_WithContractMapper_RoundTripsThroughContract()
    {
        if (!Harness.SupportedFormats.Contains(EventFormat.Protobuf))
            Assert.Ignore($"The store's test codec does not support {EventFormat.Protobuf}.");

        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<ProtoTestEvent>());
        var streamId = $"test-contract-mapper-{Guid.NewGuid()}";

        var originalEvent = new TestEvent { Value = "test-123" };

        var eventStore = CreateEventStore(
            eventTypeMap,
            eventFormat: EventFormat.Protobuf,
            contractMappers: [new TestEventProtoMapper()]);

        object[] readEvents;

        try
        {
            await eventStore.AppendAsync(streamId, [originalEvent]);
            readEvents = await eventStore.ReadStream(streamId).Select(x => x.Payload).ToArrayAsync();
        }
        finally
        {
            await eventStore.DisposeAsync();
        }

        readEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<TestEvent>()
            .ShouldBe(originalEvent);
    }

    [Test]
    public Task ReadStream_WithTransform_ReturnsTransformedEvents()
    {
        return AssertShipmentDispatchedIsTransformedAsync(new ShipmentDispatchedTransform());
    }

    [Test]
    public Task ReadStream_WithDelegateTransform_ReturnsTransformedEvents()
    {
        var transform = ReadEventTransform.Create<object, ShipmentDispatched>(e =>
        [
            new ShipmentDispatchedV2(e.ShipmentId, e.DispatchedAt),
            .. e.Packages.Select(x => new PackageShipped(e.ShipmentId, x.TrackingNumber, x.WeightKg, x.Destination))
        ]);

        return AssertShipmentDispatchedIsTransformedAsync(transform);
    }

    [Test]
    public async Task ReadStream_DelegateTransformWithIgnoredEvent_ReturnsIgnoredAtTheEventsPosition()
    {
        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<ShipmentDispatched>(),
            EventTypeMapping.ReadWrite<ShipmentDispatchedV2>());

        var streamId = $"test-read-transform-ignore-{Guid.NewGuid()}";
        var kept = new ShipmentDispatchedV2(Guid.NewGuid(), new DateTime(2025, 08, 25, 14, 30, 0, DateTimeKind.Utc));
        var retired = new ShipmentDispatched(kept.ShipmentId, kept.DispatchedAt, []);

        var eventStore = CreateEventStore(eventTypeMap)
            .WithReadTransforms(ReadEventTransform.Create<object, ShipmentDispatched>(_ => []))
            .UseIgnoredEventSentinel(IgnoredEvent.Instance);

        try
        {
            await eventStore.AppendAsync(streamId, [kept, retired]);

            var readEvents = await eventStore.ReadStream(streamId).ToArrayAsync();

            readEvents.Select(x => x.Payload).ShouldBe([kept, IgnoredEvent.Instance]);

            // The ignored head event is still observed at its own position, after the kept one.
            readEvents[1].Context.StreamPosition.ShouldNotBe(readEvents[0].Context.StreamPosition);
        }
        finally
        {
            await eventStore.DisposeAsync();
        }
    }

    [Test]
    public async Task ReadStream_DelegateTransformWithIgnoredEventAndNoIgnoredEventInstance_Throws()
    {
        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<ShipmentDispatched>());
        var streamId = $"test-read-transform-ignore-{Guid.NewGuid()}";
        var retired = new ShipmentDispatched(Guid.NewGuid(), DateTime.UtcNow, []);

        var eventStore = CreateEventStore(eventTypeMap)
            .WithReadTransforms(ReadEventTransform.Create<object, ShipmentDispatched>(_ => []));

        try
        {
            await eventStore.AppendAsync(streamId, [retired]);

            var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
                eventStore.ReadStream(streamId).ToArrayAsync().AsTask());

            ex.Message.ShouldContain(nameof(ShipmentDispatched));
        }
        finally
        {
            await eventStore.DisposeAsync();
        }
    }

    [Test]
    public async Task ReadStream_IgnoredEventPosition_IsUsedForOptimisticConcurrency()
    {
        var streamId = $"test-ignore-events-{Guid.NewGuid()}";
        var kept = new ShipmentDispatchedV2(Guid.NewGuid(), new DateTime(2025, 08, 25, 14, 30, 0, DateTimeKind.Utc));
        var next = new ShipmentDispatchedV2(Guid.NewGuid(), kept.DispatchedAt.AddHours(1));

        // Written by a store that still knows the event.
        var writer = CreateEventStore(EventTypeMap.Create(
            EventTypeMapping.ReadWrite<ShipmentDispatched>(),
            EventTypeMapping.ReadWrite<ShipmentDispatchedV2>()));

        try
        {
            await writer.AppendAsync(streamId, [kept, new ShipmentDispatched(kept.ShipmentId, kept.DispatchedAt, [])]);
        }
        finally
        {
            await writer.DisposeAsync();
        }

        // Read by a store that has no type mapping for ShipmentDispatched, only its stored name.
        var eventStore = CreateEventStore(
            EventTypeMap.Create(EventTypeMapping.ReadWrite<ShipmentDispatchedV2>()),
            ignoredEventNames: [nameof(ShipmentDispatched)]);

        try
        {
            var readEvents = await eventStore.ReadStream(streamId).ToArrayAsync();

            readEvents.Select(x => x.Payload).ShouldBe([kept, IgnoredEvent.Instance]);

            // The ignored head event's position is the stream's version, so appending at it does not conflict.
            var version = readEvents[^1].Context.StreamPosition;
            await eventStore.AppendAsync(streamId, [next], ExpectedStreamState.AtVersion(version));

            (await eventStore.ReadStream(streamId).ToArrayAsync())[^1].Payload.ShouldBe(next);
        }
        finally
        {
            await eventStore.DisposeAsync();
        }
    }

    private async Task AssertShipmentDispatchedIsTransformedAsync(IReadEventTransform<object> transform)
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

        var eventStore = CreateEventStore(eventTypeMap).WithReadTransforms(transform);

        object[] readEvents;

        try
        {
            await eventStore.AppendAsync(streamId, [legacyEvent]);

            readEvents = await eventStore
                .ReadStream(streamId)
                .Select(x => x.Payload)
                .ToArrayAsync();
        }
        finally
        {
            await eventStore.DisposeAsync();
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

    private class TestEventProtoMapper : EventContractMapper<object, TestEvent, ProtoTestEvent>
    {
        protected override ProtoTestEvent ToContract(TestEvent @event) => new() { Value = @event.Value };

        protected override TestEvent FromContract(ProtoTestEvent contract) => new() { Value = contract.Value };
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
        protected override IEnumerable<object> Apply(ShipmentDispatched @event, ReadEventInfo info)
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