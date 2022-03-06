using System;
using System.Text.Json;

namespace DomainBlocks.Serialization.Json
{
    public class JsonBytesSerializer : IJsonSerializationAdapter<ReadOnlyMemory<byte>>
    {
        public ReadOnlyMemory<byte> Serialize(object obj, JsonSerializerOptions options)
        {
            return JsonSerializer.SerializeToUtf8Bytes(obj, options);
        }

        public object Deserialize(ReadOnlyMemory<byte> rawData, Type eventType, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize(rawData.Span, eventType, options);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> rawData, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<T>(rawData.Span, options);
        }
    }
}