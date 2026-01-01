using System.Text.Json;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class SystemTextJsonStringSerializer(JsonSerializerOptions? options = null) : IObjectSerializer<string>
{
    public string Serialize(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.SerializationFailed(value.GetType(), ex);
        }
    }

    public object Deserialize(string value, Type type)
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