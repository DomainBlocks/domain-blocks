namespace DomainBlocks.EventStore.TypeMapping;

public sealed class ReadEventTypeMappingNotFoundException(string eventName) :
    EventTypeMappingNotFoundException($"Event type mapping not found for name '{eventName}'.")
{
    public string EventName { get; } = eventName;
}