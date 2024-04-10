namespace Shopping.Domain.Events;

public record ItemAddedToShoppingCart(Guid SessionId, string Item) : IDomainEvent;