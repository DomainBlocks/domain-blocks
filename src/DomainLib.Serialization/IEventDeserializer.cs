using System;
using System.Text.Json;

namespace DomainLib.Serialization
{
    public interface IEventDeserializer<in TRawData>
    {
        TEventBase DeserializeEvent<TEventBase>(TRawData eventData, string eventName, Type eventType, JsonSerializerOptions options = null);
    }
}