using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.SystemTextJson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class ObjectSerializerExtensionsTests
{
    private sealed record Payload(string Name, int Count);

    [Test]
    public void StringSerializer_RoundTripsAsJson()
    {
        var serializer = new JsonObjectSerializer().AsPostgresEventDataSerializer();
        var payload = new Payload("a", 1);

        var data = serializer.Serialize(payload);
        data.IsJson.ShouldBeTrue();

        serializer.Deserialize(data, typeof(Payload)).ShouldBe(payload);
    }

    [Test]
    public void ByteArraySerializer_RoundTripsAsBytes()
    {
        var serializer = ((IObjectSerializer<byte[]>)new JsonUtf8BytesObjectSerializer()).AsPostgresEventDataSerializer();
        var payload = new Payload("b", 2);

        var data = serializer.Serialize(payload);
        data.IsBytes.ShouldBeTrue();

        serializer.Deserialize(data, typeof(Payload)).ShouldBe(payload);
    }

    [Test]
    public void MemorySerializer_RoundTripsAsBytes()
    {
        var serializer = ((IObjectSerializer<ReadOnlyMemory<byte>>)new JsonUtf8BytesObjectSerializer()).AsPostgresEventDataSerializer();
        var payload = new Payload("c", 3);

        var data = serializer.Serialize(payload);
        data.IsBytes.ShouldBeTrue();

        serializer.Deserialize(data, typeof(Payload)).ShouldBe(payload);
    }

    [Test]
    public void ByteArraySerializer_WhenBytesAreASlice_DeserializesTheSlice()
    {
        var serializer = ((IObjectSerializer<byte[]>)new JsonUtf8BytesObjectSerializer()).AsPostgresEventDataSerializer();

        var json = "{\"Name\":\"d\",\"Count\":4}"u8.ToArray();
        byte[] padded = [0, 0, .. json, 0];
        var data = PostgresEventData.FromBytes(padded.AsMemory(2, json.Length));

        serializer.Deserialize(data, typeof(Payload)).ShouldBe(new Payload("d", 4));
    }
}