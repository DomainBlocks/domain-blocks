using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonBytesSerializer :
    IObjectSerializer<byte[]>,
    IObjectSerializer<ReadOnlyMemory<byte>>,
    IObjectSerializer<BsonValue>
{
    public byte[] Serialize(object value) => value.ToBson(value.GetType());

    public object Deserialize(byte[] value, Type type) => BsonSerializer.Deserialize(value, type);

    ReadOnlyMemory<byte> IObjectSerializer<ReadOnlyMemory<byte>>.Serialize(object value) => Serialize(value);

    object IObjectSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> value, Type type) =>
        Deserialize(value.ToArray(), type);

    BsonValue IObjectSerializer<BsonValue>.Serialize(object value) => Serialize(value);

    public object Deserialize(BsonValue value, Type type) => Deserialize(value.AsByteArray, type);
}