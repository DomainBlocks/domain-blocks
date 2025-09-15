using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class SystemTextJsonBytesSerializer(JsonSerializerOptions? options = null) :
    IPayloadSerializer<byte[]>,
    IPayloadSerializer<ReadOnlyMemory<byte>>
{
    public byte[] Serialize(object value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, options);
        }
        catch (Exception ex)
        {
            throw PayloadSerializationException.ForSerialization(value.GetType(), ex);
        }
    }

    public object Deserialize(byte[] payload, Type type)
    {
        object? result;

        try
        {
            result = JsonSerializer.Deserialize(payload, type, options);
        }
        catch (Exception ex)
        {
            throw PayloadSerializationException.ForDeserialization(type, ex);
        }

        return result ?? throw PayloadSerializationException.NullResult(type);
    }

    ReadOnlyMemory<byte> IPayloadSerializer<ReadOnlyMemory<byte>>.Serialize(object value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, options);
        }
        catch (Exception ex)
        {
            throw PayloadSerializationException.ForSerialization(value.GetType(), ex);
        }
    }

    object IPayloadSerializer<ReadOnlyMemory<byte>>.Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        object? result;

        try
        {
            result = JsonSerializer.Deserialize(payload.Span, type, options);
        }
        catch (Exception ex)
        {
            throw PayloadSerializationException.ForDeserialization(type, ex);
        }

        return result ?? throw PayloadSerializationException.NullResult(type);
    }
}