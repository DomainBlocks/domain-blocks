using System;
using System.Text.Json;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.SqlStreamStore
{
    public class SqlStreamStoreEventDeserializer : IEventDeserializer<SqlStreamStoreRawEvent>
    {
        public (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(SqlStreamStoreRawEvent rawEvent,
                                                                                   string eventName,
                                                                                   Type eventType,
                                                                                   JsonSerializerOptions options = null)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            try
            {
                var evt = JsonSerializer.Deserialize(rawEvent.EventData, eventType, options);
                var metadata = JsonSerializer.Deserialize<EventMetadata>(rawEvent.Metadata, options);

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