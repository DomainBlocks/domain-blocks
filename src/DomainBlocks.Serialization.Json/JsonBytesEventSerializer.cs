using System;
using System.Text.Json;
using DomainBlocks.Core;

namespace DomainBlocks.Serialization.Json
{
    public class JsonBytesEventSerializer : JsonEventSerializer<ReadOnlyMemory<byte>>
    {
        public JsonBytesEventSerializer(IEventNameMap eventNameMap) : 
            base(eventNameMap, new JsonBytesSerializer())
        {
        }

        public JsonBytesEventSerializer(IEventNameMap eventNameMap, JsonSerializerOptions options) : 
            base(eventNameMap, new JsonBytesSerializer(), options)
        {
        }
    }
}