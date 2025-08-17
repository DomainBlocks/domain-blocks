using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public class SystemTextJsonBytesSerializer(JsonSerializerOptions? options = null) : ISerializer<ReadOnlyMemory<byte>>
{
    public ReadOnlyMemory<byte> Serialize(object value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, options);
    }

    public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        return JsonSerializer.Deserialize(payload.Span, type, options);
    }
}