namespace DomainBlocks.EventStore;

internal sealed record EventNameToTypeMapping(string EventName, Type EventType);