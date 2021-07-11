using System;
using DomainLib.Serialization;
using EventStore.Client;

namespace DomainLib.Persistence.EventStore
{
    public class EventStoreEventPersistenceData : IEventPersistenceData<ReadOnlyMemory<byte>>
    {
        private EventStoreEventPersistenceData(Guid eventId,
                                               string eventName,
                                               string contentType,
                                               ReadOnlyMemory<byte> eventData,
                                               ReadOnlyMemory<byte> eventMetadata)
        {
            EventId = eventId;
            EventName = eventName;
            ContentType = contentType;
            EventData = eventData;
            EventMetadata = eventMetadata;
        }

        public Guid EventId { get; }
        public string EventName { get; }
        public string ContentType { get; }
        public ReadOnlyMemory<byte> EventData { get; }
        public ReadOnlyMemory<byte> EventMetadata { get; }

        public static IEventPersistenceData<ReadOnlyMemory<byte>> FromRecordedEvent(EventRecord recordedEvent)
        {
            return new EventStoreEventPersistenceData(recordedEvent.EventId.ToGuid(),
                                                      recordedEvent.EventType,
                                                      recordedEvent.ContentType,
                                                      recordedEvent.Data,
                                                      recordedEvent.Metadata);
        }
    }
}