using System.Text.Json;

namespace DomainBlocks.Experimental.Persistence.Serialization;

public class JsonStringEventDataSerializer : IEventDataSerializer<string>
{
    private readonly JsonSerializerOptions? _options;

    public JsonStringEventDataSerializer(JsonSerializerOptions? options = null)
    {
        _options = options;
    }

    public string Serialize(object value) => JsonSerializer.Serialize(value, _options);

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