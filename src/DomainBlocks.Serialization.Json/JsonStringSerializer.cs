using System;
using System.Text.Json;

namespace DomainBlocks.Serialization.Json
{
    public class JsonStringSerializer : IJsonSerializationAdapter<string>
    {
        public string Serialize(object obj, JsonSerializerOptions options)
        {
            return JsonSerializer.Serialize(obj, options);
        }

        public object Deserialize(string rawData, Type eventType, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize(rawData, eventType, options);
        }

        public T Deserialize<T>(string rawData, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<T>(rawData, options);
        }
    }
}