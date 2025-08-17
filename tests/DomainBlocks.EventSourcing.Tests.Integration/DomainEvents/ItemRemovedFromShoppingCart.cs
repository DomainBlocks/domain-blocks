namespace DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

public record ItemRemovedFromShoppingCart(Guid SessionId, string Item) : IDomainEvent;