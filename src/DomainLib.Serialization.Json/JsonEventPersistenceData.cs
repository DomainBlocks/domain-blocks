using System;

namespace DomainLib.Serialization.Json
{
    public readonly struct JsonEventPersistenceData : IEventPersistenceData
    {
        public JsonEventPersistenceData(Guid eventId, string eventName, byte[] eventData, byte[] eventMetadata) : this()
        {
            EventId = eventId;
            EventName = eventName;
            IsJsonBytes = true;
            EventData = eventData;
            EventMetadata = eventMetadata;
        }

        public Guid EventId { get; }
        public string EventName { get; }
        public bool IsJsonBytes { get; }
        public byte[] EventData { get; }
        public byte[] EventMetadata { get; }
    }
}