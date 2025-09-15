using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore;

public class EventTypeMappingNotFoundException : DomainBlocksException
{
    public EventTypeMappingNotFoundException(Type eventType) : base($"Mapping not found for event type '{eventType}'.")
    {
        EventType = eventType;
    }

    public EventTypeMappingNotFoundException(string eventName) :
        base($"Mapping not found for event name '{eventName}'.")
    {
        EventName = eventName;
    }

    public Type? EventType { get; }
    public string? EventName { get; }
}