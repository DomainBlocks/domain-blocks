using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonDocumentObjectSerializer : IObjectSerializer<BsonDocument>, IObjectSerializer<BsonValue>
{
    public BsonDocument Serialize(object value) => value.ToBsonDocument(value.GetType());

    public object Deserialize(BsonDocument data, Type type) => BsonSerializer.Deserialize(data, type);

    BsonValue IObjectSerializer<BsonValue>.Serialize(object value) => Serialize(value);

    object IObjectSerializer<BsonValue>.Deserialize(BsonValue data, Type type) =>
        Deserialize(data.AsBsonDocument, type);
}