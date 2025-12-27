using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public static class ObjectSerializerExtensions
{
    public static IObjectSerializer<BsonValue> AsBsonValueSerializer(this IObjectSerializer<byte[]> serializer)
    {
        return new BytesToBsonValueSerializer(serializer);
    }

    public static IObjectSerializer<BsonValue> AsBsonValueSerializer(this IObjectSerializer<string> serializer)
    {
        return new StringToBsonValueSerializer(serializer);
    }
}