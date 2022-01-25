using System;
using System.Text.Json;

namespace DomainBlocks.Serialization.Json
{
    public class JsonBytesEventDeserializer : IEventDeserializer<ReadOnlyMemory<byte>>
    {
        public TEventBase DeserializeEvent<TEventBase>(ReadOnlyMemory<byte> eventData, string eventName, Type eventType, JsonSerializerOptions options = null)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            try
            {
                var evt = JsonSerializer.Deserialize(eventData.Span, eventType, options);

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