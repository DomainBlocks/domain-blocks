using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class JsonUtf8BytesObjectSerializer(JsonSerializerOptions? options = null) : IByteObjectSerializer
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

    public object Deserialize(ReadOnlySpan<byte> data, Type type)
    {
        object? result;

        try
        {
            result = JsonSerializer.Deserialize(data, type, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.DeserializationFailed(type, ex);
        }

        return result ?? throw ObjectSerializationException.DeserializationReturnedNull(type);
    }
}