using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.SystemTextJson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class ObjectSerdeExtensionsTests
{
    private sealed record Payload(string Name, int Count);

    [Test]
    public void StringSerde_RoundTripsAsJson()
    {
        var serde = new JsonObjectSerde().AsPostgresEventDataSerde();
        var payload = new Payload("a", 1);

        var data = serde.Serialize(payload);
        data.IsJson.ShouldBeTrue();

        serde.Deserialize(data, typeof(Payload)).ShouldBe(payload);
    }

    [Test]
    public void ByteArraySerde_RoundTripsAsBytes()
    {
        var serde = ((IObjectSerde<byte[]>)new JsonUtf8BytesObjectSerde()).AsPostgresEventDataSerde();
        var payload = new Payload("b", 2);

        var data = serde.Serialize(payload);
        data.IsBytes.ShouldBeTrue();

        serde.Deserialize(data, typeof(Payload)).ShouldBe(payload);
    }

    [Test]
    public void MemorySerde_RoundTripsAsBytes()
    {
        var serde = ((IObjectSerde<ReadOnlyMemory<byte>>)new JsonUtf8BytesObjectSerde()).AsPostgresEventDataSerde();
        var payload = new Payload("c", 3);

        var data = serde.Serialize(payload);
        data.IsBytes.ShouldBeTrue();

        serde.Deserialize(data, typeof(Payload)).ShouldBe(payload);
    }

    [Test]
    public void ByteArraySerde_WhenBytesAreASlice_DeserializesTheSlice()
    {
        var serde = ((IObjectSerde<byte[]>)new JsonUtf8BytesObjectSerde()).AsPostgresEventDataSerde();

        var json = "{\"Name\":\"d\",\"Count\":4}"u8.ToArray();
        byte[] padded = [0, 0, .. json, 0];
        var data = PostgresEventData.FromBytes(padded.AsMemory(2, json.Length));

        serde.Deserialize(data, typeof(Payload)).ShouldBe(new Payload("d", 4));
    }
}
