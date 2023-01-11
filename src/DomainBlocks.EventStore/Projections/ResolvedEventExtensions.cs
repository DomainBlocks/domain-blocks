using DomainBlocks.Core.Projections;
using EventStore.Client;
using StreamPosition = DomainBlocks.Core.Projections.StreamPosition;

namespace DomainBlocks.EventStore.Projections;

public static class ResolvedEventExtensions
{
    public static EventNotification<EventRecord> ToEventNotification(this ResolvedEvent resolvedEvent)
    {
        var eventRecord = resolvedEvent.Event;

        return EventNotification.FromEvent(
            eventRecord, eventRecord.EventType, eventRecord.EventId.ToGuid(), StreamPosition.Empty);
    }
}