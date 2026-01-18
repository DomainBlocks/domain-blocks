namespace DomainBlocks.EventStore.TypeMapping;

internal sealed record ReadEventTypeMapping(string EventName, Type EventType);