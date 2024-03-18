using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Events;

public sealed class BytesWriteEventFactory : IWriteEventFactory
{
    public WriteEvent Create(
        string eventName, object payload, Dictionary<string, string>? metadata, IEventDataSerializer serializer)
    {
        var serializedData = serializer.SerializeToBytes(payload);
        ReadOnlyMemory<byte>? serializedMetadata = metadata == null ? null : serializer.SerializeToBytes(metadata);
        var eventData = new BytesEventData(serializedData, serializedMetadata);
        return new WriteEvent(eventName, eventData);
    }
}