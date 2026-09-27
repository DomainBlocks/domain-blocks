using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class BsonStoredNameTests
{
    private static readonly IObjectSerializer<BsonValue> Serializer = new BsonDocumentObjectSerializer();

    static BsonStoredNameTests()
    {
        BsonClassMap.RegisterClassMap<Order>(map =>
        {
            map.AutoMap();
            map.GetMemberMap(x => x.Deposit).SetSerializer(new NullableSerializer<int>(new InPenceSerializer()));
        });
    }

    [Test]
    public void GetStoredName_WhenNothingSaysOtherwise_IsTheNameOfTheMember()
    {
        StoredName<Order>(nameof(Order.Total)).ShouldBe("Total");
        StoredName<Order>(nameof(Order.Customer)).ShouldBe("Customer");
    }

    [Test]
    public void GetStoredName_WhenTheMemberIsNamed_GoesByThatName()
    {
        StoredName<Order>(nameof(Order.Reference)).ShouldBe("ref");
        StoredName<Order>(nameof(Order.Id)).ShouldBe("_id");
    }

    [Test]
    public void GetStoredName_WhenTheMemberIsNotStored_IsNull()
    {
        StoredName<Order>(nameof(Order.Secret)).ShouldBeNull();
        StoredName<Order>(nameof(Order.Describe)).ShouldBeNull();
    }

    [Test]
    public void GetStoredName_WhenASerializerOfTheCallersWritesTheValue_IsNull()
    {
        // What such a serializer stores need not compare as the value does.
        StoredName<Order>(nameof(Order.Pence)).ShouldBeNull();
    }

    [Test]
    public void GetStoredName_WhenASerializerOfTheCallersWritesTheValueOfANullable_IsNull()
    {
        // The serializer of a nullable is the driver's own, around the one that writes the value.
        StoredName<Order>(nameof(Order.Deposit)).ShouldBeNull();
        StoredName<Order>(nameof(Order.Discount)).ShouldBe("Discount");
    }

    [Test]
    public void GetStoredName_WhenTheDriverWritesANumberAsADouble_IsNull()
    {
        // A double is not the number it is read back as: 9.99 is stored a little above it, and compares so.
        StoredName<Order>(nameof(Order.Price)).ShouldBeNull();
        StoredName<Order>(nameof(Order.Weight)).ShouldBeNull();
        StoredName<Order>(nameof(Order.Tax)).ShouldBe("Tax");
    }

    [Test]
    public void GetStoredName_WhenTheMemberIsWrittenButNotReadBack_IsNull()
    {
        // It is read as its default whatever is stored, so what is stored says nothing of the event.
        StoredName<Order>(nameof(Order.Served)).ShouldBeNull();
    }

    [Test]
    public void GetStoredName_WhenTheDriverWritesTheValueAsAnotherKind_IsTheName()
    {
        // As text, which never compares with a number, so nothing is lost by looking.
        StoredName<Order>(nameof(Order.Code)).ShouldBe("Code");
    }

    [Test]
    public void GetStoredName_WhenTheTypeIsNotWrittenAsADocumentOfItsMembers_IsNull()
    {
        var length = typeof(string).GetProperty(nameof(string.Length))!;
        var count = typeof(List<int>).GetProperty(nameof(List<>.Count))!;

        Serializer.GetStoredName(typeof(string), length).ShouldBeNull();
        Serializer.GetStoredName(typeof(List<int>), count).ShouldBeNull();

        // A dictionary is written as a document, but of its keys, and one of them may be "Count".
        var countOfEntries = typeof(Dictionary<string, int>).GetProperty(nameof(Dictionary<,>.Count))!;

        Serializer.GetStoredName(typeof(Dictionary<string, int>), countOfEntries).ShouldBeNull();
    }

    [Test]
    public void GetStoredName_WhenPayloadsAreStoredAsBytesOrText_IsNull()
    {
        var total = typeof(Order).GetProperty(nameof(Order.Total))!;

        new Utf8Serializer().AsBsonValueSerializer().GetStoredName(typeof(Order), total).ShouldBeNull();
        new TextSerializer().AsBsonValueSerializer().GetStoredName(typeof(Order), total).ShouldBeNull();
    }

    private static string? StoredName<T>(string memberName) =>
        Serializer.GetStoredName(typeof(T), typeof(T).GetMember(memberName).ShouldHaveSingleItem());

    private sealed class Order
    {
        public int Id { get; init; }

        public int Total { get; init; }

        [BsonRepresentation(BsonType.String)]
        public int Code { get; init; }

        public Customer? Customer { get; init; }

        [BsonElement("ref")]
        public string? Reference { get; init; }

        [BsonIgnore]
        public string? Secret { get; init; }

        [BsonSerializer(typeof(InPenceSerializer))]
        public int Pence { get; init; }

        public int? Deposit { get; init; }

        public int? Discount { get; init; }

        [BsonRepresentation(BsonType.Double)]
        public decimal Price { get; init; }

        [BsonRepresentation(BsonType.Double, AllowTruncation = true)]
        public long? Weight { get; init; }

        public decimal Tax { get; init; }

        [BsonElement]
        public int Served { get; } = 1;

        public string Describe() => $"{Code}: {Total}";
    }

    private sealed class Customer
    {
        public string? Name { get; init; }
    }

    private sealed class InPenceSerializer : SerializerBase<int>
    {
        public override int Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
            context.Reader.ReadInt32() / 100;

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, int value) =>
            context.Writer.WriteInt32(value * 100);
    }

    private sealed class Utf8Serializer : IObjectSerializer<byte[]>
    {
        public byte[] Serialize(object value) => [];

        public object Deserialize(byte[] data, Type type) => new();
    }

    private sealed class TextSerializer : IObjectSerializer<string>
    {
        public string Serialize(object value) => "";

        public object Deserialize(string data, Type type) => new();
    }
}