using System;
using System.Text.Json;
using DomainBlocks.Serialization;
using EventStore.Client;

namespace DomainBlocks.Projections.EventStore
{
    public class EventRecordJsonDeserializer : IEventDeserializer<ResolvedEvent>
    {
        public IReadEvent<TEventBase> DeserializeEventAndMetadata<TEventBase>(ResolvedEvent rawEvent,
                                                                              string eventName,
                                                                              Type eventType,
                                                                              JsonSerializerOptions options = null)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            try
            {
                var eventRecord = rawEvent.Event;
                var evt = JsonSerializer.Deserialize(eventRecord.Data.Span, eventType, options);

                var metadata = EventMetadata.Empty;

                if (eventRecord.Metadata.Length > 0)
                {
                    metadata = JsonSerializer.Deserialize<EventMetadata>(eventRecord.Metadata.Span, options);
                }

                if (evt is TEventBase @event)
                {
                    return new ReadEvent<TEventBase>(eventRecord.EventId.ToGuid(), @event, metadata, eventRecord.EventType);
                }
            }
            catch (Exception ex)
            {
                throw new EventDeserializeException("Unable to deserialize event", ex);
            }

            var runtTimeType = typeof(TEventBase);
            throw new InvalidEventTypeException($"Cannot cast event of type {eventName} to {runtTimeType.FullName}",
                                                eventName,
                                                runtTimeType.FullName);
        }
    }
}