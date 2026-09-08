using System.Collections.Frozen;
using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class JsonMetadataSerde(JsonSerializerOptions? options = null) : IMetadataSerde<string>
{
    public string Serialize(IReadOnlyDictionary<string, string> metadata)
    {
        return JsonSerializer.Serialize(metadata, options);
    }

    public IReadOnlyDictionary<string, string> Deserialize(string metadata)
    {
        if (string.IsNullOrEmpty(metadata))
            return FrozenDictionary<string, string>.Empty;

        return JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(metadata, options) ??
               FrozenDictionary<string, string>.Empty;
    }
}
