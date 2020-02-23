using System;

namespace DomainLib.Examples.Shopping.Events
{
    public class ShoppingCartCreated : IDomainEvent
    {
        public ShoppingCartCreated(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
    }
}