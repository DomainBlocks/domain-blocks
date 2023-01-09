using System;
using System.Net.Mime;
using System.Text.Json;

namespace DomainBlocks.Core.Serialization;

public class JsonBytesEventDataSerializer : IEventDataSerializer<ReadOnlyMemory<byte>>
{
    private readonly JsonSerializerOptions _options;

    public JsonBytesEventDataSerializer(JsonSerializerOptions options = null)
    {
        _options = options;
    }

    public string ContentType => MediaTypeNames.Application.Json;
    
    public ReadOnlyMemory<byte> Serialize(object @event)
    {
        return JsonSerializer.SerializeToUtf8Bytes(@event, _options);
    }

    public object Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        try
        {
            return JsonSerializer.Deserialize(data.Span, type, _options);
        }
        catch (Exception ex)
        {
            throw new EventDeserializeException("Unable to deserialize event", ex);
        }
    }
}