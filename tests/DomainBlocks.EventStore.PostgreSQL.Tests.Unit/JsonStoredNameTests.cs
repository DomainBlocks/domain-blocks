using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.SystemTextJson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class JsonStoredNameTests
{
    [Test]
    public void GetStoredName_WhenNothingSaysOtherwise_IsTheNameOfTheMember()
    {
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Total)).ShouldBe("Total");
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Customer)).ShouldBe("Customer");
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Code)).ShouldBe("Code");
    }

    [Test]
    public void GetStoredName_WhenThereIsANamingPolicy_GoesByIt()
    {
        var serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        StoredName<Order>(serializer, nameof(Order.Total)).ShouldBe("total");
    }

    [Test]
    public void GetStoredName_WhenTheMemberIsNamed_GoesByThatName()
    {
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Reference)).ShouldBe("ref");
    }

    [Test]
    public void GetStoredName_WhenTheMemberIsNotStored_IsNull()
    {
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Secret)).ShouldBeNull();
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Describe)).ShouldBeNull();
    }

    [Test]
    public void GetStoredName_WhenAConverterOfTheCallersWritesTheValue_IsNull()
    {
        // What such a converter stores need not compare as the value does.
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Pence)).ShouldBeNull();

        var options = new JsonSerializerOptions { Converters = { new InPenceConverter() } };

        StoredName<Order>(new JsonObjectSerializer(options), nameof(Order.Total)).ShouldBeNull();
        StoredName<Order>(new JsonObjectSerializer(options), nameof(Order.Code)).ShouldBe("Code");
    }

    [Test]
    public void GetStoredName_WhenAConverterOfTheCallersWritesTheValueOfANullable_IsNull()
    {
        // The converter of a nullable is the library's own, around the one that writes the value.
        var options = new JsonSerializerOptions { Converters = { new InPenceConverter() } };

        StoredName<Order>(new JsonObjectSerializer(options), nameof(Order.Discount)).ShouldBeNull();
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Discount)).ShouldBe("Discount");
    }

    [Test]
    public void GetStoredName_WhenTheMemberIsWrittenButNotReadBack_IsNull()
    {
        // It is read as its default whatever is stored, so what is stored says nothing of the event.
        StoredName<Order>(new JsonObjectSerializer(), nameof(Order.Served)).ShouldBeNull();
    }

    [Test]
    public void GetStoredName_WhenTheMemberIsReadBackThroughTheConstructor_IsTheName()
    {
        StoredName<Parcel>(new JsonObjectSerializer(), nameof(Parcel.Weight)).ShouldBe("Weight");
    }

    [Test]
    public void GetStoredName_WhenAConverterOfTheLibraryWritesTheValue_IsTheName()
    {
        // As text, which never compares with a number, so nothing is lost by looking.
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        StoredName<Order>(new JsonObjectSerializer(options), nameof(Order.Size)).ShouldBe("Size");
    }

    [Test]
    public void GetStoredName_WhenTheTypeIsNotWrittenAsAnObject_IsNull()
    {
        var length = typeof(string).GetProperty(nameof(string.Length))!;
        var count = typeof(List<int>).GetProperty(nameof(List<>.Count))!;

        new JsonObjectSerializer().GetStoredName(typeof(string), length).ShouldBeNull();
        new JsonObjectSerializer().GetStoredName(typeof(List<int>), count).ShouldBeNull();

        var options = new JsonSerializerOptions { Converters = { new CustomerConverter() } };

        StoredName<Customer>(new JsonObjectSerializer(options), nameof(Customer.Name)).ShouldBeNull();
    }

    [Test]
    public void GetStoredName_WhenJsonIsStoredAsJsonb_IsAsTheJsonSerializerSays()
    {
        var serializer = new JsonObjectSerializer().AsPostgresEventDataSerializer();

        StoredName<Order>(serializer, nameof(Order.Reference)).ShouldBe("ref");
    }

    [Test]
    public void GetStoredName_WhenPayloadsAreStoredAsBytes_IsNull()
    {
        var serializer = new Utf8Serializer().AsPostgresEventDataSerializer();

        StoredName<Order>(serializer, nameof(Order.Total)).ShouldBeNull();
    }

    private static string? StoredName<T>(IObjectSerializer<string> serializer, string memberName) =>
        serializer.GetStoredName(typeof(T), Member<T>(memberName));

    private static string? StoredName<T>(IObjectSerializer<PostgresEventData> serializer, string memberName) =>
        serializer.GetStoredName(typeof(T), Member<T>(memberName));

    private static MemberInfo Member<T>(string name) => typeof(T).GetMember(name).ShouldHaveSingleItem();

    private enum Size
    {
        Small
    }

    private sealed class Order
    {
        public int Total { get; init; }

        public string? Code { get; init; }

        public Size Size { get; init; }

        public int? Discount { get; init; }

        public int Served { get; private set; }

        public Customer? Customer { get; init; }

        [JsonPropertyName("ref")]
        public string? Reference { get; init; }

        [JsonIgnore]
        public string? Secret { get; init; }

        [JsonConverter(typeof(InPenceConverter))]
        public int Pence { get; init; }

        public string Describe() => $"{Code}: {Total}";
    }

    private sealed class Customer
    {
        public string? Name { get; init; }
    }

    private sealed class Parcel(int weight)
    {
        public int Weight { get; } = weight;
    }

    private sealed class InPenceConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetInt32() / 100;

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value * 100);
    }

    private sealed class CustomerConverter : JsonConverter<Customer>
    {
        public override Customer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new() { Name = reader.GetString() };

        public override void Write(Utf8JsonWriter writer, Customer value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Name);
    }

    private sealed class Utf8Serializer : IObjectSerializer<byte[]>
    {
        public byte[] Serialize(object value) => JsonSerializer.SerializeToUtf8Bytes(value);

        public object Deserialize(byte[] data, Type type) => JsonSerializer.Deserialize(data, type)!;
    }
}