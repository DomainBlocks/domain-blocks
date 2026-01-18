namespace DomainBlocks.EventStore.TypeMapping;

internal sealed record AppendEventTypeMapping(Type EventType, string EventName);