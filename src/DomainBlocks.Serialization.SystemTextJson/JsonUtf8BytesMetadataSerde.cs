using System.Collections.Frozen;
using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class JsonUtf8BytesMetadataSerde(JsonSerializerOptions? options = null) :
    IMetadataSerde<byte[]>,
    IMetadataSerde<ReadOnlyMemory<byte>>,
    IByteMetadataDeserializer
{
    public byte[] Serialize(IReadOnlyDictionary<string, string> metadata)
    {
        return JsonSerializer.SerializeToUtf8Bytes(metadata, options);
    }

    public IReadOnlyDictionary<string, string> Deserialize(ReadOnlySpan<byte> metadata)
    {
        if (metadata.IsEmpty)
            return FrozenDictionary<string, string>.Empty;

        return JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(metadata, options) ??
               FrozenDictionary<string, string>.Empty;
    }

    ReadOnlyMemory<byte> IMetadataSerializer<ReadOnlyMemory<byte>>.Serialize(
        IReadOnlyDictionary<string, string> metadata)
    {
        return Serialize(metadata);
    }
}