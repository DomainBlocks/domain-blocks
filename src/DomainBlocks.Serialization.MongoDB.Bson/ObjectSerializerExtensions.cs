using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public static class ObjectSerializerExtensions
{
    /// <summary>
    /// Adapts a byte array serializer so that event data is stored as a BSON binary value.
    /// </summary>
    public static IObjectSerializer<BsonValue> AsBsonValueSerializer(this IObjectSerializer<byte[]> serializer)
    {
        return new BytesToBsonValueSerializer(serializer);
    }

    /// <summary>
    /// Adapts a string serializer so that event data is stored as a BSON string value.
    /// </summary>
    public static IObjectSerializer<BsonValue> AsBsonValueSerializer(this IObjectSerializer<string> serializer)
    {
        return new StringToBsonValueSerializer(serializer);
    }
}