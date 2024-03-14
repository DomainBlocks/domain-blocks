using DomainBlocks.Experimental.Persistence.Adapters;
using EventStore.Client;

namespace DomainBlocks.Experimental.Persistence.EventStoreDb;

public sealed class EventStoreDbEventAdapter :
    IEventAdapter<ResolvedEvent, EventData, StreamRevision, ReadOnlyMemory<byte>>
{
    public string GetEventName(ResolvedEvent readEvent) => readEvent.Event.EventType;
    public Task<ReadOnlyMemory<byte>> GetEventData(ResolvedEvent readEvent) => Task.FromResult(readEvent.Event.Data);

    public bool TryGetMetadata(ResolvedEvent readEvent, out ReadOnlyMemory<byte> metadata)
    {
        if (readEvent.Event.Metadata.IsEmpty)
        {
            metadata = default;
            return false;
        }

        metadata = readEvent.Event.Metadata;
        return true;
    }

    public StreamRevision GetStreamVersion(ResolvedEvent readEvent) =>
        StreamRevision.FromStreamPosition(readEvent.OriginalEventNumber);

    public EventData CreateWriteEvent(
        string eventName,
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<byte> metadata = default,
        string? contentType = null)
    {
        ReadOnlyMemory<byte>? nullableMetadata = metadata.Equals(default) ? null : metadata;

        return contentType == null
            ? new EventData(Uuid.NewUuid(), eventName, data, nullableMetadata)
            : new EventData(Uuid.NewUuid(), eventName, data, nullableMetadata, contentType);
    }
}