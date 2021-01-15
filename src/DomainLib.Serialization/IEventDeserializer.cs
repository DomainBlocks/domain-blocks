using System;
using System.Text.Json;

namespace DomainLib.Serialization
{
    public interface IEventDeserializer
    {
        TEventBase DeserializeEvent<TEventBase>(ReadOnlySpan<byte> eventData, string eventName, Type eventType, JsonSerializerOptions options = null);
    }
}