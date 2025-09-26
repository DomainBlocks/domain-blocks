using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonBytesSerializer :
    IPayloadSerializer<byte[]>,
    IPayloadSerializer<ReadOnlyMemory<byte>>,
    IPayloadSerializer<BsonValue>
{
    public byte[] Serialize(object value) => value.ToBson(value.GetType());

    public object Deserialize(byte[] payload, Type type) => BsonSerializer.Deserialize(payload, type);

    ReadOnlyMemory<byte> IPayloadSerializer<ReadOnlyMemory<byte>>.Serialize(object value) => Serialize(value);

    object IPayloadSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> payload, Type type) =>
        Deserialize(payload.ToArray(), type);

    BsonValue IPayloadSerializer<BsonValue>.Serialize(object value) => Serialize(value);

    public object Deserialize(BsonValue payload, Type type) => Deserialize(payload.AsByteArray, type);
}