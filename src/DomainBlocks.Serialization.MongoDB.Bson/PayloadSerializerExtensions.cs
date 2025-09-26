using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public static class PayloadSerializerExtensions
{
    public static IPayloadSerializer<BsonValue> AsBsonValueSerializer(this IPayloadSerializer<byte[]> serializer)
    {
        return new BytePayloadToBsonValueAdapter(serializer);
    }

    public static IPayloadSerializer<BsonValue> AsBsonValueSerializer(this IPayloadSerializer<string> serializer)
    {
        return new StringPayloadToBsonValueAdapter(serializer);
    }
}