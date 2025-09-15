namespace DomainBlocks.EventStore;

public class EventTypeMapping(Type eventType, string? eventName = null)
{
    public Type EventType { get; } = eventType;
    public string EventName { get; } = eventName ?? eventType.Name;
}