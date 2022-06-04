using System;

namespace Shopping.Events;

public class ItemAddedToShoppingCart : IDomainEvent
{
    public const string EventName = "ItemAddedToShoppingCart";

    public ItemAddedToShoppingCart(Guid id, Guid cartId, string item)
    {
        Id = id;
        CartId = cartId;
        Item = item;
    }

    public Guid Id { get; }
    public Guid CartId { get; }
    public string Item { get; }
    
    public void Accept(IDomainEventVisitor visitor) => visitor.Visit(this);
}