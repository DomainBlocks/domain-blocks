using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

/// <summary>
/// Serializes metadata as a JSON string, for stores whose metadata column holds text (e.g. PostgreSQL <c>jsonb</c>).
/// </summary>
public sealed class JsonMetadataSerializer(JsonSerializerOptions? options = null) : IMetadataSerializer<string>
{
    private readonly JsonWriterOptions _writerOptions = MetadataJsonWriter.GetWriterOptions(options);

    public string Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        return Encoding.UTF8.GetString(MetadataJsonWriter.Write(metadata, _writerOptions));
    }

    public IReadOnlyDictionary<string, string> Deserialize(string data)
    {
        if (string.IsNullOrEmpty(data))
            return FrozenDictionary<string, string>.Empty;

        return JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(data, options) ??
               FrozenDictionary<string, string>.Empty;
    }
}