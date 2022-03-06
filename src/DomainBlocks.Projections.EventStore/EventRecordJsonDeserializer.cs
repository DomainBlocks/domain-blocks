using System;
using System.Text.Json;
using DomainBlocks.Serialization;
using EventStore.Client;

namespace DomainBlocks.Projections.EventStore
{
    public class EventRecordJsonDeserializer : IEventDeserializer<EventRecord>
    {
        public (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(EventRecord rawEvent,
                                                                                   string eventName,
                                                                                   Type eventType,
                                                                                   JsonSerializerOptions options = null)
        {
            if (rawEvent == null) throw new ArgumentNullException(nameof(rawEvent));
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            try
            {
                var evt = JsonSerializer.Deserialize(rawEvent.Data.Span, eventType, options);
                var metadata = JsonSerializer.Deserialize<EventMetadata>(rawEvent.Metadata.Span, options);

                if (evt is TEventBase @event)
                {
                    return (@event, metadata);
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