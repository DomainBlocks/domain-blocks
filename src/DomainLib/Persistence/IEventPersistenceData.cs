using System;

namespace DomainLib.Persistence
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