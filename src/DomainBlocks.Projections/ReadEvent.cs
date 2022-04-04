using System;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections
{
    public sealed class ReadEvent<TEventBase> : IReadEvent<TEventBase>
    {
        public ReadEvent(Guid id, TEventBase payload, EventMetadata metadata, string eventType)
        {
            Id = id;
            Payload = payload;
            Metadata = metadata;
            EventType = eventType;
        }

        public Guid Id { get; }
        public TEventBase Payload { get; }
        public EventMetadata Metadata { get; }
        public string EventType { get; }
    }
}