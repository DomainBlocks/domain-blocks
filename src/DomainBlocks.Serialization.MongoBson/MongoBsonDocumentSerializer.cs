using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoBson;

public class MongoBsonDocumentSerializer : ISerializer<BsonDocument>
{
    public BsonDocument Serialize(object value)
    {
        return value.ToBsonDocument();
    }

    public object? Deserialize(BsonDocument payload, Type type)
    {
        return BsonSerializer.Deserialize(payload, type);
    }
}