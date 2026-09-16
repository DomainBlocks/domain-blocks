using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BytesToBsonValueSerializer(IObjectSerializer<byte[]> serializer) : IObjectSerializer<BsonValue>
{
    public BsonValue Serialize(object value) => serializer.Serialize(value);

    public object Deserialize(BsonValue data, Type type) => serializer.Deserialize(data.AsByteArray, type);
}