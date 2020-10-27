using System;
using DomainLib.Aggregates;

namespace Shopping.Domain.Events
{
    [EventName("ShoppingCartCreated")]
    public class ShoppingCartCreated : IDomainEvent
    {
        public ShoppingCartCreated(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
        
    }
}