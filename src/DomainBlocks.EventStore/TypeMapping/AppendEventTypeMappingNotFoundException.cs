namespace DomainBlocks.EventStore.TypeMapping;

public sealed class AppendEventTypeMappingNotFoundException(Type eventType) :
    EventTypeMappingNotFoundException($"Event name mapping not found for type '{eventType}'.")
{
    public Type EventType { get; } = eventType;
}