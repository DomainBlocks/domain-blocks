using System;
using System.Collections.Generic;
using DomainBlocks.Serialization;
using EventStore.Client;

namespace DomainBlocks.Persistence.EventStore
{
    public static class EventStoreEventDataExtensions
    {
        public static EventData ToEventData(this IEventSerializer<ReadOnlyMemory<byte>> @eventSerializer, object @event, string eventNameOverride = null, params KeyValuePair<string, string>[] additionalMetadata)
        {
            var eventPersistenceData = eventSerializer.GetPersistenceData(@event, eventNameOverride, additionalMetadata);
            return new EventData(Uuid.FromGuid(eventPersistenceData.EventId),
                                 eventPersistenceData.EventName,
                                 eventPersistenceData.EventData,
                                 eventPersistenceData.EventMetadata,
                                 eventPersistenceData.ContentType);
        }
    }
}