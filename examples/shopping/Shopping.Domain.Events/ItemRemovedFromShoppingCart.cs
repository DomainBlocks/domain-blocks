namespace Shopping.Domain.Events;

public record ItemRemovedFromShoppingCart(Guid SessionId, string Item) : IDomainEvent;