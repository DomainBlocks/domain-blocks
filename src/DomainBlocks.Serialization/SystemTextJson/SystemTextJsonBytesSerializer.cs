using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class SystemTextJsonBytesSerializer(JsonSerializerOptions? options = null) :
    ISerializer<byte[]>,
    ISerializer<ReadOnlyMemory<byte>>
{
    public byte[] Serialize(object value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, options);
    }

    public object? Deserialize(byte[] payload, Type type)
    {
        return JsonSerializer.Deserialize(payload, type, options);
    }

    ReadOnlyMemory<byte> ISerializer<ReadOnlyMemory<byte>>.Serialize(object value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, options);
    }

    object? ISerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        return JsonSerializer.Deserialize(payload.Span, type, options);
    }
}