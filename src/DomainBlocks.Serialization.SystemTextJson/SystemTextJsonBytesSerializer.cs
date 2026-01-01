using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class SystemTextJsonBytesSerializer(JsonSerializerOptions? options = null) :
    IObjectSerializer<byte[]>,
    IObjectSerializer<ReadOnlyMemory<byte>>
{
    public byte[] Serialize(object value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.SerializationFailed(value.GetType(), ex);
        }
    }

    public object Deserialize(byte[] value, Type type) => Deserialize(value.AsSpan(), type);

    ReadOnlyMemory<byte> IObjectSerializer<ReadOnlyMemory<byte>>.Serialize(object value) => Serialize(value);

    object IObjectSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> value, Type type) =>
        Deserialize(value.Span, type);

    private object Deserialize(ReadOnlySpan<byte> value, Type type)
    {
        object? result;

        try
        {
            result = JsonSerializer.Deserialize(value, type, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.DeserializationFailed(type, ex);
        }

        return result ?? throw ObjectSerializationException.DeserializationReturnedNull(type);
    }
}