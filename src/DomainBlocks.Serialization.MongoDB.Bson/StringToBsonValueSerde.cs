using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class StringToBsonValueSerde(IObjectSerde<string> serde) : IObjectSerde<BsonValue>
{
    public BsonValue Serialize(object value) => serde.Serialize(value);

    public object Deserialize(BsonValue value, Type type) => serde.Deserialize(value.AsString, type);
}