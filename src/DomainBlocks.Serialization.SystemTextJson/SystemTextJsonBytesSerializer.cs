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
            throw ObjectSerializationException.ForSerialization(value.GetType(), ex);
        }
    }

    public object Deserialize(byte[] value, Type type)
    {
        object? result;

        try
        {
            result = JsonSerializer.Deserialize(value, type, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.ForDeserialization(type, ex);
        }

        return result ?? throw ObjectSerializationException.NullResult(type);
    }

    ReadOnlyMemory<byte> IObjectSerializer<ReadOnlyMemory<byte>>.Serialize(object value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.ForSerialization(value.GetType(), ex);
        }
    }

    object IObjectSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> value, Type type)
    {
        object? result;

        try
        {
            result = JsonSerializer.Deserialize(value.Span, type, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.ForDeserialization(type, ex);
        }

        return result ?? throw ObjectSerializationException.NullResult(type);
    }
}