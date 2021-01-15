using System;
using System.Text.Json;

namespace DomainLib.Serialization.Json
{
    public class JsonEventDeserializer : IEventDeserializer
    {
        public TEventBase DeserializeEvent<TEventBase>(ReadOnlySpan<byte> eventData, string eventName, Type eventType, JsonSerializerOptions options = null)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            try
            {
                var evt = JsonSerializer.Deserialize(eventData, eventType, options);

                if (evt is TEventBase @event)
                {
                    return @event;
                }
            }
            catch (Exception ex)
            {
                throw new EventDeserializeException("Unable to deserialize event", ex);
            }

            var runtTimeType = typeof(TEventBase);
            throw new InvalidEventTypeException($"Cannot cast event of type {eventName} to {runtTimeType.FullName}", eventName, runtTimeType.FullName);
        }
    }
}