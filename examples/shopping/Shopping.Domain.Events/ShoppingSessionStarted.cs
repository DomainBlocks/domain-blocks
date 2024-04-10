namespace Shopping.Domain.Events;

public record ShoppingSessionStarted(Guid SessionId) : IDomainEvent;