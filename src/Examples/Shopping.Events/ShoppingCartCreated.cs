using System;

namespace Shopping.Events;

public class ShoppingCartCreated : IDomainEvent
{
    public const string EventName = "ShoppingCartCreated";

    public ShoppingCartCreated(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }
    
    public void Accept(IDomainEventVisitor visitor) => visitor.Visit(this);
}