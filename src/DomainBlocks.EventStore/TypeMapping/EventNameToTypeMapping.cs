namespace DomainBlocks.EventStore.TypeMapping;

internal sealed record EventNameToTypeMapping(string EventName, Type EventType);