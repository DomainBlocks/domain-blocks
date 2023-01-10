using System;
using System.Net.Mime;
using System.Text.Json;

namespace DomainBlocks.Core.Serialization;

public class JsonStringEventDataSerializer : IEventDataSerializer<string>
{
    private readonly JsonSerializerOptions _options;

    public JsonStringEventDataSerializer(JsonSerializerOptions options = null)
    {
        _options = options;
    }

    public string ContentType => MediaTypeNames.Application.Json;

    public string Serialize(object @event)
    {
        return JsonSerializer.Serialize(@event, _options);
    }

    public object Deserialize(string data, Type type)
    {
        try
        {
            return JsonSerializer.Deserialize(data, type, _options);
        }
        catch (Exception ex)
        {
            throw new EventDeserializeException("Unable to deserialize event", ex);
        }
    }
}