using System;

namespace DomainBlocks.Serialization
{
    public interface IReadEvent<out TEventBase>
    {
        Guid Id { get; }
        TEventBase Payload { get; }
        EventMetadata Metadata { get; }
        string EventType { get; }
    }
}