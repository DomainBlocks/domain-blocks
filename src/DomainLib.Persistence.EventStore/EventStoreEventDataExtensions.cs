using EventStore.ClientAPI;

namespace DomainLib.Persistence.EventStore
{
    public static class EventStoreEventDataExtensions
    {
        public static EventData ToEventData(this IEventSerializer @eventSerializer, object @event)
        {
            var eventPersistenceData = eventSerializer.GetPersistenceData(@event);
            return new EventData(eventPersistenceData.EventId, eventPersistenceData.EventName, eventPersistenceData.IsJsonBytes, eventPersistenceData.EventData, eventPersistenceData.EventMetadata);
        }
    }
}