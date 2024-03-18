using DomainBlocks.Experimental.Persistence.Adapters;
using DomainBlocks.Experimental.Persistence.Serialization;
using EventStore.Client;

namespace DomainBlocks.Experimental.Persistence.EventStoreDb;

public sealed class EventStoreDbEventAdapter : IEventAdapter<ResolvedEvent, EventData>
{
    public string GetEventName(ResolvedEvent readEvent) => readEvent.Event.EventType;

    public ValueTask<object> Deserialize(ResolvedEvent readEvent, Type type, IEventDataSerializer serializer)
    {
        var obj = serializer.Deserialize(readEvent.Event.Data.Span, type);
        return ValueTask.FromResult(obj);
    }

    public Dictionary<string, string> DeserializeMetadata(ResolvedEvent readEvent, IEventDataSerializer serializer)
    {
        if (readEvent.Event.Metadata.IsEmpty)
        {
            return new Dictionary<string, string>();
        }

        return (Dictionary<string, string>)serializer.Deserialize(
            readEvent.Event.Metadata.Span, typeof(Dictionary<string, string>));
    }

    public StreamVersion GetStreamVersion(ResolvedEvent readEvent) =>
        StreamVersion.FromUInt64(readEvent.OriginalEventNumber);

    public EventData CreateWriteEvent(
        string eventName, object payload, Dictionary<string, string>? metadata, IEventDataSerializer serializer)
    {
        var data = serializer.SerializeToBytes(payload);
        ReadOnlyMemory<byte>? rawMetadata = metadata == null ? null : serializer.SerializeToBytes(metadata);

        // return contentType == null
        //     ? new EventData(Uuid.NewUuid(), eventName, data, rawMetadata)
        //     : new EventData(Uuid.NewUuid(), eventName, data, rawMetadata, contentType);

        return new EventData(Uuid.NewUuid(), eventName, data, rawMetadata);
    }
}