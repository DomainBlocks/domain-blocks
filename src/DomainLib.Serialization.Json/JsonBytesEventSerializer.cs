using System.Text.Json;
using DomainLib.Aggregates;

namespace DomainLib.Serialization.Json
{
    public class JsonBytesEventSerializer : JsonEventSerializer<byte[]>
    {
        public JsonBytesEventSerializer(IEventNameMap eventNameMap) : 
            base(eventNameMap, new ByteArrayEventSerializationAdapter())
        {
        }

        public JsonBytesEventSerializer(IEventNameMap eventNameMap, JsonSerializerOptions options) : 
            base(eventNameMap, new ByteArrayEventSerializationAdapter(), options)
        {
        }
    }
}