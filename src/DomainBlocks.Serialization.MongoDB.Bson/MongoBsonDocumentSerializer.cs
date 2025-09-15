using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class MongoBsonDocumentSerializer : IPayloadSerializer<BsonDocument>
{
    public BsonDocument Serialize(object value) => value.ToBsonDocument(value.GetType());

    public object Deserialize(BsonDocument payload, Type type) => BsonSerializer.Deserialize(payload, type);
}