using System.Runtime.Serialization;
using System.Text.Json;
using SystemJsonSerializer = System.Text.Json.JsonSerializer;

namespace DomainBlocks.Persistence.Serialization;

public class JsonSerializer : ISerializer
{
    private readonly JsonSerializerOptions? _options;

    public JsonSerializer(JsonSerializerOptions? options = null)
    {
        _options = options;
    }

    public byte[] Serialize(object value) => SystemJsonSerializer.SerializeToUtf8Bytes(value, _options);

    public object Deserialize(ReadOnlySpan<byte> data, Type type)
    {
        try
        {
            return SystemJsonSerializer.Deserialize(data, type, _options) ??
                   throw new SerializationException("Event deserialize result was null.");
        }
        catch (Exception ex)
        {
            throw new SerializationException("Unable to deserialize event.", ex);
        }
    }
}