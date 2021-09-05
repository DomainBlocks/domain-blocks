using System;

namespace DomainBlocks.Serialization
{
    public interface IEventPersistenceData<out TRawData>
    {
        Guid EventId { get; }
        string EventName { get; }
        string ContentType { get; }
        TRawData EventData { get; }
        TRawData EventMetadata { get; }
    }
}