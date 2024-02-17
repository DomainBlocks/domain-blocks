namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public delegate bool EventReadCondition(IReadOnlyDictionary<string, string> metadata);