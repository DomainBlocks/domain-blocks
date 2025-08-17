using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public class MongoBsonBytesSerializer : ISerializer<ReadOnlyMemory<byte>>
{
    public ReadOnlyMemory<byte> Serialize(object value)
    {
        return value.ToBsonDocument(value.GetType()).ToBson();
    }

    public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        var bsonDocument = BsonSerializer.Deserialize<BsonDocument>(payload.ToArray());
        return BsonSerializer.Deserialize(bsonDocument, type);
    }
}