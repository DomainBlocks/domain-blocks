using System;

namespace DomainBlocks.Serialization;

public interface IEventDeserializer<in TRawData>
{
    (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(
        TRawData rawEvent,
        string eventName,
        Type eventType);
}