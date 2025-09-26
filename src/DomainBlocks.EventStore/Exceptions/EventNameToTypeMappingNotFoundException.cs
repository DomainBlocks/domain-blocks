namespace DomainBlocks.EventStore.Exceptions;

public sealed class EventNameToTypeMappingNotFoundException(string eventName) :
    EventTypeMappingNotFoundException($"Event type mapping not found for name '{eventName}'.")
{
    public string EventName { get; } = eventName;
}