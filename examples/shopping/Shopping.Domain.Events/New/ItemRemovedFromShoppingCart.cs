using System;

namespace Shopping.Domain.Events.New;

public record ItemRemovedFromShoppingCart(Guid SessionId, string Item) : IDomainEvent;