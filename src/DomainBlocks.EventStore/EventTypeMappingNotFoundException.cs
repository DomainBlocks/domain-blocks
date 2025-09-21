using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore;

public sealed class EventTypeMappingNotFoundException : DomainBlocksException
{
    public EventTypeMappingNotFoundException(Type eventType) :
        base($"Event name mapping not found for type '{eventType}'.")
    {
        EventType = eventType;
    }

    public EventTypeMappingNotFoundException(string eventName) :
        base($"Event type mapping not found for name '{eventName}'.")
    {
        EventName = eventName;
    }

    public Type? EventType { get; }
    public string? EventName { get; }
}