using System;
using System.Text.Json;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.SqlStreamStore;

public class StreamMessageJsonDeserializer : IEventDeserializer<StreamMessageWrapper>
{
    private readonly JsonSerializerOptions _serializerOptions;

    public StreamMessageJsonDeserializer(JsonSerializerOptions serializerOptions = null)
    {
        _serializerOptions = serializerOptions;
    }

    public (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(
        StreamMessageWrapper streamMessage,
        string eventName,
        Type eventType)
    {
        if (eventName == null) throw new ArgumentNullException(nameof(eventName));
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));

        try
        {
            var evt = JsonSerializer.Deserialize(streamMessage.JsonData, eventType, _serializerOptions);

            var metadata = EventMetadata.Empty;
            if (streamMessage.JsonMetadata != null)
            {
                metadata = JsonSerializer.Deserialize<EventMetadata>(streamMessage.JsonMetadata, _serializerOptions);
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