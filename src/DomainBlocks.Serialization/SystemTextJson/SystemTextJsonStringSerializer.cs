using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class SystemTextJsonStringSerializer(JsonSerializerOptions? options = null) : ISerializer<string>
{
    public string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, options);
    }

    public object? Deserialize(string payload, Type type)
    {
        return JsonSerializer.Deserialize(payload, type, options);
    }
}