namespace DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

public record ShoppingSessionStarted(Guid SessionId) : IDomainEvent;