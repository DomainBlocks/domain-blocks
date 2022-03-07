using System;
using System.Text.Json;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.SqlStreamStore
{
    public class StreamMessageJsonDeserializer : IEventDeserializer<StreamMessageWrapper>
    {
        public (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(StreamMessageWrapper streamMessage,
                                                                                   string eventName,
                                                                                   Type eventType,
                                                                                   JsonSerializerOptions options = null)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            try
            {
                var evt = JsonSerializer.Deserialize(streamMessage.JsonData, eventType, options);

                var metadata = EventMetadata.Empty;
                if (streamMessage.JsonMetadata != null)
                {
                    metadata = JsonSerializer.Deserialize<EventMetadata>(streamMessage.JsonMetadata, options);
                }
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