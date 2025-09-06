using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class MongoBsonBytesSerializer : IPayloadSerializer<byte[]>, IPayloadSerializer<ReadOnlyMemory<byte>>
{
    public byte[] Serialize(object value)
    {
        return value.ToBsonDocument(value.GetType()).ToBson();
    }

    public object Deserialize(byte[] payload, Type type)
    {
        var bsonDocument = BsonSerializer.Deserialize<BsonDocument>(payload);
        return BsonSerializer.Deserialize(bsonDocument, type);
    }

    ReadOnlyMemory<byte> IPayloadSerializer<ReadOnlyMemory<byte>>.Serialize(object value)
    {
        return Serialize(value);
    }

    object IPayloadSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        return Deserialize(payload.ToArray(), type);
    }
}