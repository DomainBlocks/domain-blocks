using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public static class ObjectSerdeExtensions
{
    public static IObjectSerde<BsonValue> AsBsonValueSerde(this IObjectSerde<byte[]> serde)
    {
        return new BytesToBsonValueSerde(serde);
    }

    public static IObjectSerde<BsonValue> AsBsonValueSerde(this IObjectSerde<string> serde)
    {
        return new StringToBsonValueSerde(serde);
    }
}