using System.Collections.Generic;
using DomainLib.Serialization;
using EventStore.ClientAPI;

namespace DomainLib.Persistence.EventStore
{
    public static class EventStoreEventDataExtensions
    {
        public static EventData ToEventData(this IEventSerializer @eventSerializer, object @event, string eventNameOverride = null, params KeyValuePair<string, string>[] additionalMetadata)
        {
            var eventPersistenceData = eventSerializer.GetPersistenceData(@event, eventNameOverride, additionalMetadata);
            return new EventData(eventPersistenceData.EventId,
                                 eventPersistenceData.EventName,
                                 eventPersistenceData.IsJsonBytes,
                                 eventPersistenceData.EventData,
                                 eventPersistenceData.EventMetadata);
        }
    }
}