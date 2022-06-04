using System;

namespace Shopping.Events;

public class ItemRemovedFromShoppingCart : IDomainEvent
{
    public const string EventName = "ItemRemovedFromShoppingCart";

    public ItemRemovedFromShoppingCart(Guid id, Guid cartId)
    {
        Id = id;
        CartId = cartId;
    }

    public Guid Id { get; }
    public Guid CartId { get; }
    
    public void Accept(IDomainEventVisitor visitor) => visitor.Visit(this);
}