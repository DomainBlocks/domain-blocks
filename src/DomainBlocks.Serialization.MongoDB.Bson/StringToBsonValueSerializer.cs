using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class StringToBsonValueSerializer(IObjectSerializer<string> serializer) : IObjectSerializer<BsonValue>
{
    public BsonValue Serialize(object value) => serializer.Serialize(value);

    public object Deserialize(BsonValue value, Type type) => serializer.Deserialize(value.AsString, type);
}