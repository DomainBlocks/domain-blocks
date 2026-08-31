using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

public sealed class EventTypeNotMappedException(Type eventType) :
    DomainBlocksException($"Event type '{eventType.Name}' is not mapped to an event name.")
{
    public Type EventType { get; } = eventType;
}