using System.Text.Json;

namespace DomainBlocks.Experimental.Persistence.Serialization;

public class JsonEventDataSerializer : IEventDataSerializer
{
    private readonly JsonSerializerOptions? _options;

    public JsonEventDataSerializer(JsonSerializerOptions? options = null)
    {
        _options = options;
    }

    public SerializationFormat Format => SerializationFormat.Json;

    public byte[] Serialize(object value) => JsonSerializer.SerializeToUtf8Bytes(value, _options);

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
}