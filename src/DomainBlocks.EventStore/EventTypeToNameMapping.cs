namespace DomainBlocks.EventStore;

internal sealed record EventTypeToNameMapping(Type EventType, string EventName);