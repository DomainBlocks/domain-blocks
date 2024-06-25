using DomainBlocks.V1.Abstractions;
using EventStore.Client;
using StreamPosition = DomainBlocks.V1.Abstractions.StreamPosition;

namespace DomainBlocks.V1.EventStoreDb.Extensions;

public static class ResolvedEventExtensions
{
    public static StoredEventRecord ToStoredEventRecord(this ResolvedEvent resolvedEvent)
    {
        var streamPosition = new StreamPosition(resolvedEvent.OriginalEvent.EventNumber);
        var globalPosition = new GlobalPosition(resolvedEvent.OriginalEvent.Position.CommitPosition);

        return new StoredEventRecord(
            resolvedEvent.Event.EventType,
            resolvedEvent.Event.Data,
            resolvedEvent.Event.Metadata,
            streamPosition,
            globalPosition);
    }
}