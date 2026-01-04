namespace DomainBlocks.EventStore.TypeMapping;

internal sealed record EventTypeToNameMapping(Type EventType, string EventName);