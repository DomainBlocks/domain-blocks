using System.Text.Json;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Serialization.Json
{
    public class JsonStringEventSerializer : JsonEventSerializer<string>
    {
        public JsonStringEventSerializer(IEventNameMap eventNameMap) : 
            base(eventNameMap, new JsonStringSerializer())
        {
        }

        public JsonStringEventSerializer(IEventNameMap eventNameMap, JsonSerializerOptions options) :
            base(eventNameMap, new JsonStringSerializer(), options)
        {
        }
    }
}