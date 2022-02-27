using System;
using System.Collections.Generic;
using System.Text.Json;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.EventStore
{
    public class EventStoreEventDeserializer : IEventDeserializer<EventStoreRawEvent>
    {
        public (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(EventStoreRawEvent rawEvent,
                                                                                    string eventName,
                                                                                    Type eventType,
                                                                                    JsonSerializerOptions options = null)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            try
            {
                var evt = JsonSerializer.Deserialize(rawEvent.EventData.Span, eventType, options);
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