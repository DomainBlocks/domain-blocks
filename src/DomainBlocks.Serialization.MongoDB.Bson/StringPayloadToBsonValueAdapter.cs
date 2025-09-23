using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class StringPayloadToBsonValueAdapter(IPayloadSerializer<string> serializer) :
    IPayloadSerializer<BsonValue>
{
    public BsonValue Serialize(object value) => serializer.Serialize(value);

    public object Deserialize(BsonValue payload, Type type) => serializer.Deserialize(payload.AsString, type);
}