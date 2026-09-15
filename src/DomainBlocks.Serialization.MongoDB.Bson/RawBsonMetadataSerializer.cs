using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class RawBsonMetadataSerializer : IMetadataSerializer<byte[]>
{
    public byte[] Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        using var stream = new MemoryStream();
        using var writer = new BsonBinaryWriter(stream);

        writer.WriteStartDocument();

        foreach (var (key, value) in metadata)
        {
            writer.WriteName(key);
            writer.WriteString(value);
        }

        writer.WriteEndDocument();

        return stream.ToArray();
    }

    public IReadOnlyDictionary<string, string> Deserialize(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BsonBinaryReader(stream);

        return BsonSerializer.Deserialize<Dictionary<string, string>>(reader);
    }
}