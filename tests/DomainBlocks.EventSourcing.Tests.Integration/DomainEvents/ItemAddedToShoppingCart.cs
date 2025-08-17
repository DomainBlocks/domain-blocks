namespace DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

public record ItemAddedToShoppingCart(Guid SessionId, string Item) : IDomainEvent;