using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BytesToBsonValueSerde(IObjectSerde<byte[]> serde) : IObjectSerde<BsonValue>
{
    public BsonValue Serialize(object value) => serde.Serialize(value);

    public object Deserialize(BsonValue value, Type type) => serde.Deserialize(value.AsByteArray, type);
}