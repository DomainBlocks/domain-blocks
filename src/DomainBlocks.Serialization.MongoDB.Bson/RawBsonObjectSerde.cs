using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class RawBsonObjectSerde : IObjectSerde<byte[]>
{
    public byte[] Serialize(object value)
    {
        return value.ToBson(value.GetType());
    }

    public object Deserialize(byte[] value, Type type)
    {
        return BsonSerializer.Deserialize(value, type);
    }
}