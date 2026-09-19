using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Codecs;

public class EventCodecBuilderTests
{
    [Test]
    public void Build_WithMappings_RoundTripsAnEvent()
    {
        var codec = BuildWithFakes(Builder().MapEvents(EventTypeMapping.ReadWrite<OrderPlaced>()));

        var encoded = codec.Encode(new OrderPlaced("o1"), []);
        encoded.EventName.ShouldBe(nameof(OrderPlaced));

        codec.Decode(encoded.EventName, encoded.EventData, null).Payload.ShouldBe(new OrderPlaced("o1"));
    }

    [Test]
    public void MapEvent_DefaultsNameToTypeName_AndAcceptsOverride()
    {
        var codec = BuildWithFakes(Builder().MapEvent<OrderPlaced>().MapEvent<OrderShipped>("Shipped"));

        codec.Encode(new OrderPlaced("o1"), []).EventName.ShouldBe(nameof(OrderPlaced));
        codec.Encode(new OrderShipped("o1"), []).EventName.ShouldBe("Shipped");
    }

    [Test]
    public void MapEvents_CalledRepeatedly_Accumulates()
    {
        var codec = BuildWithFakes(Builder()
            .MapEvents(EventTypeMapping.ReadWrite<OrderPlaced>())
            .MapEvents(EventTypeMapping.ReadWrite<OrderShipped>()));

        codec.Encode(new OrderShipped("o1"), []).EventName.ShouldBe(nameof(OrderShipped));
    }

    [Test]
    public void UseEventTypeMap_UsesThePrebuiltMap()
    {
        var map = EventTypeMap.Create(EventTypeMapping.ReadWrite<OrderPlaced>("Placed"));

        var codec = BuildWithFakes(Builder().UseEventTypeMap(map));

        codec.Encode(new OrderPlaced("o1"), []).EventName.ShouldBe("Placed");
    }

    [Test]
    public void MapEvents_AfterUseEventTypeMap_Throws()
    {
        var builder = Builder().UseEventTypeMap(EventTypeMap.Create(EventTypeMapping.ReadWrite<OrderPlaced>()));

        Should.Throw<InvalidOperationException>(() => builder.MapEvents(EventTypeMapping.ReadWrite<OrderShipped>()));
    }

    [Test]
    public void UseEventTypeMap_AfterMapEvents_Throws()
    {
        var builder = Builder().MapEvents(EventTypeMapping.ReadWrite<OrderPlaced>());

        Should.Throw<InvalidOperationException>(() =>
            builder.UseEventTypeMap(EventTypeMap.Create(EventTypeMapping.ReadWrite<OrderShipped>())));
    }

    [Test]
    public void Build_WithoutAnyMapping_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() => BuildWithFakes(Builder()));

        ex.Message.ShouldContain("No event types are mapped");
    }

    [Test]
    public void Build_WithoutSerializers_ThrowsNamingTheMethodToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Builder().MapEvent<OrderPlaced>().Build());

        ex.Message.ShouldContain("UseEventSerializer");
    }

    [Test]
    public void Build_WithDefaults_UsesDefaultsOnlyForSerializersNotConfigured()
    {
        var defaultEvent = new FakeObjectSerializer();
        var defaultMetadata = new FakeMetadataSerializer();
        var explicitMetadata = new FakeMetadataSerializer();
        var defaultEventUsed = false;
        var defaultMetadataUsed = false;

        var codec = Builder()
            .MapEvent<OrderPlaced>()
            .UseMetadataSerializer(explicitMetadata)
            .Build(
                () =>
                {
                    defaultEventUsed = true;
                    return defaultEvent;
                },
                () =>
                {
                    defaultMetadataUsed = true;
                    return defaultMetadata;
                });

        defaultEventUsed.ShouldBeTrue();
        defaultMetadataUsed.ShouldBeFalse();
        codec.Encode(new OrderPlaced("o1"), [new("k", "v")]).Metadata.ShouldBe("k=v");
    }

    [Test]
    public void AddContractMappers_RegistersTheContractTypeUnderItsTypeName()
    {
        var codec = BuildWithFakes(Builder().MapEvent<OrderPlaced>().AddContractMappers(new OrderShippedMapper()));

        var encoded = codec.Encode(new OrderShipped("o1"), []);

        encoded.EventName.ShouldBe(nameof(OrderShippedContract));
        encoded.EventData.ShouldBe("OrderShippedContract:o1");
        codec.Decode(encoded.EventName, encoded.EventData, null).Payload.ShouldBe(new OrderShipped("o1"));
    }

    [Test]
    public void AddContractMappers_WhenContractTypeMappedExplicitly_KeepsTheExplicitName()
    {
        var codec = BuildWithFakes(Builder()
            .MapEvents(EventTypeMapping.ReadWrite<OrderShippedContract>("Shipped"))
            .AddContractMappers(new OrderShippedMapper()));

        codec.Encode(new OrderShipped("o1"), []).EventName.ShouldBe("Shipped");
    }

    private static EventCodecBuilder<object, string, string> Builder() => new();

    private static IEventCodec<object, string, string> BuildWithFakes(
        EventCodecBuilder<object, string, string> builder)
    {
        return builder
            .UseEventSerializer(new FakeObjectSerializer())
            .UseMetadataSerializer(new FakeMetadataSerializer())
            .Build();
    }

    private sealed record OrderPlaced(string OrderId);

    private sealed record OrderShipped(string OrderId);

    private sealed record OrderShippedContract(string OrderId);

    private sealed class OrderShippedMapper : EventContractMapper<object, OrderShipped, OrderShippedContract>
    {
        protected override OrderShippedContract ToContract(OrderShipped @event) => new(@event.OrderId);

        protected override OrderShipped FromContract(OrderShippedContract contract) => new(contract.OrderId);
    }
}