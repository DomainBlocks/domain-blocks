namespace DomainBlocks.EventStore.Exceptions;

public sealed class EventTypeToNameMappingNotFoundException(Type eventType) :
    EventTypeMappingException($"Event name mapping not found for type '{eventType}'.")
{
    public Type EventType { get; } = eventType;
}