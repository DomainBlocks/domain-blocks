using System;
using System.Text.Json;

namespace DomainBlocks.Serialization
{
    public interface IEventDeserializer<in TRawData>
    {
        (TEventBase, EventMetadata) DeserializeEventAndMetadata<TEventBase>(TRawData rawEvent,
                                                                            string eventName,
                                                                            Type eventType,
                                                                            JsonSerializerOptions options = null);
    }
}