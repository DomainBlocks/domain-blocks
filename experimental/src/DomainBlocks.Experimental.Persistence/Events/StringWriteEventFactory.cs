using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Events;

public sealed class StringWriteEventFactory : IWriteEventFactory
{
    public WriteEvent Create(
        string eventName, object payload, Dictionary<string, string>? metadata, IEventDataSerializer serializer)
    {
        var serializedData = serializer.SerializeToString(payload);
        var serializedMetadata = metadata == null ? null : serializer.SerializeToString(metadata);
        var eventData = new StringEventData(serializedData, serializedMetadata);
        return new WriteEvent(eventName, eventData);
    }
}