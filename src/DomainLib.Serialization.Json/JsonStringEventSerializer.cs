﻿using System.Text.Json;
using DomainLib.Aggregates;

namespace DomainLib.Serialization.Json
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