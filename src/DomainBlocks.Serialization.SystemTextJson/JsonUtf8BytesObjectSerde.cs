using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class JsonUtf8BytesObjectSerde(JsonSerializerOptions? options = null) :
    IObjectSerde<byte[]>,
    IObjectSerde<ReadOnlyMemory<byte>>,
    IByteObjectDeserializer
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

    public object Deserialize(ReadOnlySpan<byte> value, Type type)
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

    ReadOnlyMemory<byte> IObjectSerializer<ReadOnlyMemory<byte>>.Serialize(object value) => Serialize(value);
}