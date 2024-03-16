using System.Net.Mime;
using System.Text.Json;

namespace DomainBlocks.Experimental.Persistence.Serialization;

public class JsonEventDataSerializer : IEventDataSerializer
{
    private readonly JsonSerializerOptions? _options;

    public JsonEventDataSerializer(JsonSerializerOptions? options = null)
    {
        _options = options;
    }

    public string ContentType => MediaTypeNames.Application.Json;

    public byte[] SerializeToBytes(object value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, _options);
    }

    public string SerializeToString(object value)
    {
        return JsonSerializer.Serialize(value, _options);
    }

    public object Deserialize(ReadOnlySpan<byte> data, Type type)
    {
        try
        {
            return JsonSerializer.Deserialize(data, type, _options) ??
                   throw new EventDeserializeException("Event deserialize result was null");
        }
        catch (Exception ex)
        {
            throw new EventDeserializeException("Unable to deserialize event", ex);
        }
    }

    public object Deserialize(string data, Type type)
    {
        try
        {
            return JsonSerializer.Deserialize(data, type, _options) ??
                   throw new EventDeserializeException("Event deserialize result was null");
        }
        catch (Exception ex)
        {
            throw new EventDeserializeException("Unable to deserialize event", ex);
        }
    }
}