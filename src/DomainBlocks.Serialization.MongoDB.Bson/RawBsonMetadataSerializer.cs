using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class RawBsonMetadataSerializer : IMetadataSerializer<byte[]>
{
    public byte[] Serialize(IReadOnlyDictionary<string, string> metadata)
    {
        using var stream = new MemoryStream();
        using var writer = new BsonBinaryWriter(stream);

        BsonSerializer.Serialize(writer, metadata);

        return stream.ToArray();
    }

    public IReadOnlyDictionary<string, string> Deserialize(byte[] metadata)
    {
        using var stream = new MemoryStream(metadata);
        using var reader = new BsonBinaryReader(stream);

        return BsonSerializer.Deserialize<Dictionary<string, string>>(reader);
    }
}