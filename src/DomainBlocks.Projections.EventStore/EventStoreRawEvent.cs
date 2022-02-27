using System;
using EventStore.Client;

namespace DomainBlocks.Projections.EventStore
{
    public struct EventStoreRawEvent
    {
        public EventStoreRawEvent(ReadOnlyMemory<byte> eventData, ReadOnlyMemory<byte> metadata)
        {
            EventData = eventData;
            Metadata = metadata;
        }

        public ReadOnlyMemory<byte> EventData { get; set; }
        public ReadOnlyMemory<byte> Metadata { get; set; }

        public static EventStoreRawEvent FromResolvedEvent(ResolvedEvent resolvedEvent)
        {
            return new EventStoreRawEvent(resolvedEvent.Event.Data, resolvedEvent.Event.Metadata);
        }
    }
}