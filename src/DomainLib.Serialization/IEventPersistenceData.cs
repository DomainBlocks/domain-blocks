using System;

namespace DomainLib.Serialization
{
    public interface IEventPersistenceData<out TRawData>
    {
        Guid EventId { get; }
        string EventName { get; }
        bool IsJson { get; }
        TRawData EventData { get; }
        TRawData EventMetadata { get; }
    }
}