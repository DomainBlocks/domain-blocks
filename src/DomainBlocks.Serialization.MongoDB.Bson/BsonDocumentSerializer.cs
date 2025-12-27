using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonDocumentSerializer : IObjectSerializer<BsonDocument>, IObjectSerializer<BsonValue>
{
    public BsonDocument Serialize(object value) => value.ToBsonDocument(value.GetType());

    public object Deserialize(BsonDocument value, Type type) => BsonSerializer.Deserialize(value, type);

    BsonValue IObjectSerializer<BsonValue>.Serialize(object value) => Serialize(value);

    object IObjectSerializer<BsonValue>.Deserialize(BsonValue value, Type type) =>
        Deserialize(value.AsBsonDocument, type);
}