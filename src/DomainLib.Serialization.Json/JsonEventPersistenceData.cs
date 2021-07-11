using System;
using System.Net.Mime;

namespace DomainLib.Serialization.Json
{
    public readonly struct JsonEventPersistenceData<TRawData> : IEventPersistenceData<TRawData>
    {
        public JsonEventPersistenceData(Guid eventId, string eventName, TRawData eventData, TRawData eventMetadata) : this()
        {
            EventId = eventId;
            EventName = eventName;
            ContentType = MediaTypeNames.Application.Json;
            EventData = eventData;
            EventMetadata = eventMetadata;
        }

        public Guid EventId { get; }
        public string EventName { get; }
        public string ContentType { get; }
        public TRawData EventData { get; }
        public TRawData EventMetadata { get; }
    }
}