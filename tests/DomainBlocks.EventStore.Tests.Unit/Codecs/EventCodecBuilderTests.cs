using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Codecs;

public class EventCodecBuilderTests
{
    [Test]
    public void MapEvent_DefaultsNameToTypeName_AndAcceptsOverride()
    {
        var codec = BuildWithFakes(Builder().MapEvent<OrderPlaced>().MapEvent<OrderShipped>("Shipped"));

        codec.Encode(new OrderPlaced("o1"), []).EventName.ShouldBe(nameof(OrderPlaced));
        codec.Encode(new OrderShipped("o1"), []).EventName.ShouldBe("Shipped");
    }

    [Test]
    public void UseEventTypeMap_UsesThePrebuiltMap()
    {
        var map = new EventTypeMapBuilder().Add<OrderPlaced>("Placed").Build();

        var codec = BuildWithFakes(Builder().UseEventTypeMap(map));

        codec.Encode(new OrderPlaced("o1"), []).EventName.ShouldBe("Placed");
    }

    [Test]
    public void MapEvent_AfterUseEventTypeMap_Throws()
    {
        var builder = Builder().UseEventTypeMap(new EventTypeMapBuilder().Add<OrderPlaced>().Build());

        Should.Throw<InvalidOperationException>(() => builder.MapEvent<OrderShipped>());
    }

    [Test]
    public void UseEventTypeMap_AfterMapEvent_Throws()
    {
        var builder = Builder().MapEvent<OrderPlaced>();

        Should.Throw<InvalidOperationException>(() =>
            builder.UseEventTypeMap(new EventTypeMapBuilder().Add<OrderShipped>().Build()));
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
        codec.Encode(new OrderPlaced("o1"), [KeyValuePair.Create("k", "v")]).Metadata.ShouldBe("k=v");
    }

    private static EventCodecBuilder<object, string, string> Builder() => new();

    private static IEventCodec<object, string, string> BuildWithFakes(EventCodecBuilder<object, string, string> builder)
    {
        return builder
            .UseEventSerializer(new FakeObjectSerializer())
            .UseMetadataSerializer(new FakeMetadataSerializer())
            .Build();
    }

    private sealed record OrderPlaced(string OrderId);

    private sealed record OrderShipped(string OrderId);
}