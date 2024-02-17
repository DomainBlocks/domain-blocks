using DomainBlocks.Experimental.EventSourcing.Persistence;
using EventStore.Client;

namespace DomainBlocks.EventStore.Experimental;

public sealed class EventStoreEventAdapter :
    IEventAdapter<ResolvedEvent, EventData, ReadOnlyMemory<byte>, StreamRevision>
{
    public string GetEventName(ResolvedEvent readEvent) => readEvent.Event.EventType;
    public Task<ReadOnlyMemory<byte>> GetEventData(ResolvedEvent readEvent) => Task.FromResult(readEvent.Event.Data);
    public ReadOnlyMemory<byte> GetEventMetadata(ResolvedEvent readEvent) => readEvent.Event.Metadata;

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