using System;

namespace DomainLib.Serialization
{
    public interface IEventPersistenceData
    {
        Guid EventId { get; }
        string EventName { get; }
        bool IsJsonBytes { get; }
        byte[] EventData { get; }
        byte[] EventMetadata { get; }
    }
}