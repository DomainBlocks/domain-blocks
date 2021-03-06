using EventStore.Client;

namespace DomainBlocks.Projections.EventStore
{
    public static class ResolvedEventExtensions
    {
        public static EventNotification<EventRecord> ToEventNotification (this ResolvedEvent resolvedEvent)
        {
            var eventRecord = resolvedEvent.Event;
            return EventNotification.FromEvent(eventRecord, eventRecord.EventType, eventRecord.EventId.ToGuid());
        }
    }
}