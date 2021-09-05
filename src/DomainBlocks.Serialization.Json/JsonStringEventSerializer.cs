using System.Text.Json;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Serialization.Json
{
    public class JsonStringEventSerializer : JsonEventSerializer<string>
    {
        public JsonStringEventSerializer(IEventNameMap eventNameMap) : 
            base(eventNameMap, new Utf8StringEventSerializationAdapter())
        {
        }

        public JsonStringEventSerializer(IEventNameMap eventNameMap, JsonSerializerOptions options) :
            base(eventNameMap, new Utf8StringEventSerializationAdapter(), options)
        {
        }
    }
}