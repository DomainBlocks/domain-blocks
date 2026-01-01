using System.Collections.Frozen;
using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class SystemTextJsonBytesMetadataSerializer(JsonSerializerOptions? options = null) :
    IMetadataSerializer<byte[]>,
    IMetadataSerializer<ReadOnlyMemory<byte>>
{
    public byte[] Serialize(IReadOnlyDictionary<string, string> metadata)
    {
        return JsonSerializer.SerializeToUtf8Bytes(metadata, options);
    }

    public IReadOnlyDictionary<string, string> Deserialize(byte[] metadata)
    {
        return Deserialize(metadata.AsSpan());
    }

    ReadOnlyMemory<byte> IMetadataSerializer<ReadOnlyMemory<byte>>.Serialize(
        IReadOnlyDictionary<string, string> metadata)
    {
        return Serialize(metadata);
    }

    IReadOnlyDictionary<string, string> IMetadataSerializer<ReadOnlyMemory<byte>>.Deserialize(
        ReadOnlyMemory<byte> metadata)
    {
        return Deserialize(metadata.Span);
    }

    private IReadOnlyDictionary<string, string> Deserialize(ReadOnlySpan<byte> metadata)
    {
        if (metadata.IsEmpty)
            return FrozenDictionary<string, string>.Empty;

        return JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(metadata, options) ??
               FrozenDictionary<string, string>.Empty;
    }
}