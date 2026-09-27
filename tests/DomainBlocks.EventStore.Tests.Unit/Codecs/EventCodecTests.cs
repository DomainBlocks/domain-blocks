using System.Linq.Expressions;
using System.Reflection;
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

            // Stores a member under its name in lower case, and not one of a type from elsewhere, such as a string.
            EventSerializer = new FakeObjectSerializer
            {
                StoredNames = (type, member) =>
                    type.DeclaringType == typeof(EventCodecTests) ? member.Name.ToLowerInvariant() : null
            },
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

    [Test]
    public void GetEventNames_WhenATypeIsReadUnderSeveralNames_ReturnsThemAll()
    {
        CreateCodec().ResolveEventNames(typeof(OrderPlaced)).ShouldBe(["OrderPlaced", "OrderPlacedV1"], ignoreOrder: true);
    }

    [Test]
    public void GetEventNames_WhenGivenABaseType_ReturnsTheNamesOfWhatDerivesFromIt()
    {
        CreateCodec().ResolveEventNames(typeof(IOrderEvent)).ShouldBe(["OrderPlaced", "OrderPlacedV1"], ignoreOrder: true);

        CreateCodec().ResolveEventNames(typeof(object))
            .ShouldBe(["OrderPlaced", "OrderPlacedV1", "OrderShipped"], ignoreOrder: true);
    }

    [Test]
    public void GetEventNames_WhenNothingIsReadAsTheType_ReturnsNone()
    {
        CreateCodec().ResolveEventNames(typeof(string)).ShouldBeEmpty();
    }

    [Test]
    public void GetEventNames_WhenAContractMapperReadsTheName_GoesByTheEventItMapsTo()
    {
        var codec = CreateCodec(new OrderShippedMapper());

        codec.ResolveEventNames(typeof(OrderShipped)).ShouldBe(["OrderShipped"]);
        codec.ResolveEventNames(typeof(OrderShippedContract)).ShouldBeEmpty();
    }

    [Test]
    public void GetStoredPath_WhenTheTypeIsStoredAsItself_JoinsTheStoredNames()
    {
        var codec = CreateCodec();

        codec.ResolveStoredPath(typeof(OrderPlaced), Members((OrderPlaced e) => e.OrderId)).ShouldBe("orderid");
        codec.ResolveStoredPath(typeof(OrderPlaced), Members((OrderPlaced e) => e.Customer!.Name)).ShouldBe("customer.name");
    }

    [Test]
    public void GetStoredPath_WhenTheSerializerDoesNotSayForAMember_IsNull()
    {
        var members = Members((OrderPlaced e) => e.Customer!.Name!.Length);

        CreateCodec().ResolveStoredPath(typeof(OrderPlaced), members).ShouldBeNull();
    }

    [Test]
    public void GetStoredPath_WhenAContractIsStoredInPlaceOfTheEvent_IsNull()
    {
        var members = Members((OrderShipped e) => e.OrderId);

        CreateCodec(new OrderShippedMapper()).ResolveStoredPath(typeof(OrderShipped), members).ShouldBeNull();
    }

    [Test]
    public void GetStoredPath_WhenOtherTypesAreReadAsTheType_IsNull()
    {
        // What a member of an interface is stored under is up to each type that has it.
        var members = Members((IOrderEvent e) => e.OrderId);

        CreateCodec().ResolveStoredPath(typeof(IOrderEvent), members).ShouldBeNull();
    }

    [Test]
    public void GetStoredPath_WhenNothingIsReadAsTheType_IsNull()
    {
        CreateCodec().ResolveStoredPath(typeof(NotMapped), Members((NotMapped e) => e.OrderId)).ShouldBeNull();
    }

    [Test]
    public void GetStoredPath_WhenAStoredNameHasADot_IsNull()
    {
        var codec = EventCodec.Create(new EventCodecOptions<object, string, string>
        {
            TypeMap = TypeMap,
            EventSerializer = new FakeObjectSerializer { StoredNames = (_, _) => "order.id" },
            MetadataSerializer = new FakeMetadataSerializer()
        });

        codec.ResolveStoredPath(typeof(OrderPlaced), Members((OrderPlaced e) => e.OrderId)).ShouldBeNull();
    }

    [Test]
    public void GetStoredPath_WhenThereAreNoMembers_IsNull()
    {
        CreateCodec().ResolveStoredPath(typeof(OrderPlaced), []).ShouldBeNull();
    }

    // The members that an expression goes through, outermost last.
    private static MemberInfo[] Members<TEvent, TValue>(Expression<Func<TEvent, TValue>> path)
    {
        var members = new List<MemberInfo>();

        for (var e = path.Body; e is MemberExpression access; e = access.Expression)
            members.Insert(0, access.Member);

        return [.. members];
    }

    private interface IOrderEvent
    {
        string OrderId { get; }
    }

    private sealed record OrderPlaced(string OrderId) : IOrderEvent
    {
        public Customer? Customer { get; init; }
    }

    private sealed record Customer(string? Name);

    private sealed record NotMapped(string OrderId);

    private sealed record OrderShipped(string OrderId);

    private sealed record OrderShippedContract(string OrderId);

    private sealed class OrderShippedMapper : EventContractMapper<object, OrderShipped, OrderShippedContract>
    {
        protected override OrderShippedContract ToContract(OrderShipped @event) => new(@event.OrderId);

        protected override OrderShipped FromContract(OrderShippedContract contract) => new(contract.OrderId);
    }
}