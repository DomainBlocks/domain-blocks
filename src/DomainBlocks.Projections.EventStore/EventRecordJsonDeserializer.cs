using System;
using System.Text.Json;
using DomainBlocks.Serialization;
using EventStore.Client;

namespace DomainBlocks.Projections.EventStore;

public class EventRecordJsonDeserializer : IEventDeserializer<EventRecord>
{
    private readonly JsonSerializerOptions _serializerOptions;

    public EventRecordJsonDeserializer(JsonSerializerOptions serializerOptions = null)
    {
        _serializerOptions = serializerOptions;
    }

    public (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(EventRecord rawEvent,
        string eventName,
        Type eventType)
    {
        if (rawEvent == null) throw new ArgumentNullException(nameof(rawEvent));
        if (eventName == null) throw new ArgumentNullException(nameof(eventName));
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));

        try
        {
            var evt = JsonSerializer.Deserialize(rawEvent.Data.Span, eventType, _serializerOptions);

            var metadata = EventMetadata.Empty;

            if (rawEvent.Metadata.Length > 0)
            {
                metadata = JsonSerializer.Deserialize<EventMetadata>(rawEvent.Metadata.Span, _serializerOptions);
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
        throw new InvalidEventTypeException(eventName, runtTimeType.FullName);
    }
}