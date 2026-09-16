using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Codecs;

public class EventCodecTests
{
    private static readonly EventTypeMap TypeMap = EventTypeMap.Create(
        EventTypeMapping.ReadWrite<OrderPlaced>(),
        EventTypeMapping.ReadWrite<OrderShippedContract>("OrderShipped"),
        EventTypeMapping.ReadOnly<OrderPlaced>("OrderPlacedV1"));

    private static EventCodec<object, string, string> CreateCodec(params IEventContractMapper<object>[] mappers)
    {
        return EventCodec.Create(new EventCodecOptions<object, string, string>
        {
            TypeMap = TypeMap,
            EventSerializer = new FakeObjectSerializer(),
            MetadataSerializer = new FakeMetadataSerializer(),
            ContractMappers = mappers
        });
    }

    [Test]
    public void Encode_ResolvesNameFromTypeMapAndSerializesPayload()
    {
        var encoded = CreateCodec().Encode(new OrderPlaced("o1"), []);

        encoded.EventName.ShouldBe(nameof(OrderPlaced));
        encoded.EventData.ShouldBe("OrderPlaced:o1");
    }

    [Test]
    public void Encode_NoMetadata_LeavesMetadataDefault()
    {
        CreateCodec().Encode(new OrderPlaced("o1"), []).Metadata.ShouldBeNull();
    }

    [Test]
    public void Encode_WithMetadata_SerializesTheGivenSpanInOrder()
    {
        KeyValuePair<string, string>[] metadata = [new("b", "2"), new("a", "1")];

        CreateCodec().Encode(new OrderPlaced("o1"), metadata).Metadata.ShouldBe("b=2;a=1");
    }

    [Test]
    public void Encode_WithContractMapper_NamesAndSerializesTheContract()
    {
        var encoded = CreateCodec(new OrderShippedMapper()).Encode(new OrderShipped("o1"), []);

        encoded.EventName.ShouldBe("OrderShipped");
        encoded.EventData.ShouldBe("OrderShippedContract:o1");
    }

    [Test]
    public void Encode_UnmappedType_Throws()
    {
        Should.Throw<EventTypeNotMappedException>(() => CreateCodec().Encode(new OrderShipped("o1"), []));
    }

    [Test]
    public void Decode_ResolvesTypeFromNameAndDeserializes()
    {
        var (payload, metadata) = CreateCodec().Decode(nameof(OrderPlaced), "OrderPlaced:o1", null);

        payload.ShouldBe(new OrderPlaced("o1"));
        metadata.ShouldBeEmpty();
    }

    [Test]
    public void Decode_ReadOnlyName_ResolvesToMappedType()
    {
        CreateCodec().Decode("OrderPlacedV1", "OrderPlaced:o1", null).Payload.ShouldBe(new OrderPlaced("o1"));
    }

    [Test]
    public void Decode_WithMetadata_Deserializes()
    {
        var (_, metadata) = CreateCodec().Decode(nameof(OrderPlaced), "OrderPlaced:o1", "a=1;b=2");

        metadata.ShouldBe(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
    }

    [Test]
    public void Decode_WithContractMapper_MapsContractBackToDomainEvent()
    {
        var (payload, _) = CreateCodec(new OrderShippedMapper())
            .Decode("OrderShipped", "OrderShippedContract:o1", null);

        payload.ShouldBe(new OrderShipped("o1"));
    }

    [Test]
    public void Decode_UnmappedName_Throws()
    {
        Should.Throw<EventNameNotMappedException>(() => CreateCodec().Decode("Nope", "x", null));
    }

    [Test]
    public void Decode_DeserializedTypeNotAssignableToEventType_Throws()
    {
        var codec = EventCodec.Create(new EventCodecOptions<OrderPlaced, string, string>
        {
            TypeMap = TypeMap,
            EventSerializer = new FakeObjectSerializer(),
            MetadataSerializer = new FakeMetadataSerializer()
        });

        Should.Throw<InvalidCastException>(() => codec.Decode("OrderShipped", "OrderShippedContract:o1", null));
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