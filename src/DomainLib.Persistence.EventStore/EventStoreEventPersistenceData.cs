using System;
using DomainLib.Serialization;
using EventStore.ClientAPI;

namespace DomainLib.Persistence.EventStore
{
    public class EventStoreEventPersistenceData : IEventPersistenceData
    {
        private EventStoreEventPersistenceData(Guid eventId, string eventName, bool isJsonBytes, byte[] eventData, byte[] eventMetadata)
        {
            EventId = eventId;
            EventName = eventName;
            IsJsonBytes = isJsonBytes;
            EventData = eventData;
            EventMetadata = eventMetadata;
        }

        public Guid EventId { get; }
        public string EventName { get; }
        public bool IsJsonBytes { get; }
        public byte[] EventData { get; }
        public byte[] EventMetadata { get; }

        public static IEventPersistenceData FromRecordedEvent(RecordedEvent recordedEvent)
        {
            return new EventStoreEventPersistenceData(recordedEvent.EventId,
                                                      recordedEvent.EventType,
                                                      recordedEvent.IsJson,
                                                      recordedEvent.Data,
                                                      recordedEvent.Metadata);
        }
    }
}