using System.Collections.Frozen;
using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

/// <summary>
/// Serializes metadata as UTF-8 JSON bytes.
/// </summary>
public sealed class JsonUtf8BytesMetadataSerializer(JsonSerializerOptions? options = null) : IByteMetadataSerializer
{
    private readonly JsonWriterOptions _writerOptions = MetadataJsonWriter.GetWriterOptions(options);

    public byte[] Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        return MetadataJsonWriter.Write(metadata, _writerOptions).ToArray();
    }

    public IReadOnlyDictionary<string, string> Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return FrozenDictionary<string, string>.Empty;

        return JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(data, options) ??
               FrozenDictionary<string, string>.Empty;
    }
}