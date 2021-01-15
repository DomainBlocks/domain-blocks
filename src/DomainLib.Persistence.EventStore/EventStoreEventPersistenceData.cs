using System;
using DomainLib.Serialization;
using EventStore.ClientAPI;

namespace DomainLib.Persistence.EventStore
{
    public class EventStoreEventPersistenceData : IEventPersistenceData<byte[]>
    {
        private EventStoreEventPersistenceData(Guid eventId, string eventName, bool isJsonBytes, byte[] eventData, byte[] eventMetadata)
        {
            EventId = eventId;
            EventName = eventName;
            IsJson = isJsonBytes;
            EventData = eventData;
            EventMetadata = eventMetadata;
        }

        public Guid EventId { get; }
        public string EventName { get; }
        public bool IsJson { get; }
        public byte[] EventData { get; }
        public byte[] EventMetadata { get; }

        public static IEventPersistenceData<byte[]> FromRecordedEvent(RecordedEvent recordedEvent)
        {
            return new EventStoreEventPersistenceData(recordedEvent.EventId,
                                                      recordedEvent.EventType,
                                                      recordedEvent.IsJson,
                                                      recordedEvent.Data,
                                                      recordedEvent.Metadata);
        }
    }
}