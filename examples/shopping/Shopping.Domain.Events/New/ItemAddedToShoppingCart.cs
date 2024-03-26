using System;

namespace Shopping.Domain.Events.New;

public record ItemAddedToShoppingCart(Guid SessionId, string Item) : IDomainEvent;