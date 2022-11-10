using System;

namespace Shopping.Domain.Events;

public class ItemRemovedFromShoppingCart : IDomainEvent
{
    public ItemRemovedFromShoppingCart(Guid id, Guid cartId)
    {
        Id = id;
        CartId = cartId;
    }

    public Guid Id { get; }
    public Guid CartId { get; }
}