using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class SystemTextJsonStringSerializer(JsonSerializerOptions? options = null) : IPayloadSerializer<string>
{
    public string Serialize(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, options);
        }
        catch (Exception ex)
        {
            throw PayloadSerializationException.ForSerialization(value.GetType(), ex);
        }
    }

    public object Deserialize(string payload, Type type)
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
}