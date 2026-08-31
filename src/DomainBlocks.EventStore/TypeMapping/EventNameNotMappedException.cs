using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

public sealed class EventNameNotMappedException(string eventName) :
    DomainBlocksException($"Event name '{eventName}' is not mapped to an event type.")
{
    public string EventName { get; } = eventName;
}