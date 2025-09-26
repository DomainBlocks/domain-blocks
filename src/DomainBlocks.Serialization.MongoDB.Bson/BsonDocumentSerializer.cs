using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonDocumentSerializer : IPayloadSerializer<BsonDocument>, IPayloadSerializer<BsonValue>
{
    public BsonDocument Serialize(object value) => value.ToBsonDocument(value.GetType());

    public object Deserialize(BsonDocument payload, Type type) => BsonSerializer.Deserialize(payload, type);

    BsonValue IPayloadSerializer<BsonValue>.Serialize(object value) => Serialize(value);

    object IPayloadSerializer<BsonValue>.Deserialize(BsonValue payload, Type type) =>
        Deserialize(payload.AsBsonDocument, type);
}