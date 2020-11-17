using System.Collections.Generic;
using EventStore.ClientAPI;

namespace DomainLib.Persistence.EventStore
{
    public static class EventStoreEventDataExtensions
    {
        public static EventData ToEventData(this IEventSerializer @eventSerializer, object @event, params KeyValuePair<string, string>[] additionalMetadata)
        {
            var eventPersistenceData = eventSerializer.GetPersistenceData(@event, additionalMetadata);
            return new EventData(eventPersistenceData.EventId, eventPersistenceData.EventName, eventPersistenceData.IsJsonBytes, eventPersistenceData.EventData, eventPersistenceData.EventMetadata);
        }
    }
}