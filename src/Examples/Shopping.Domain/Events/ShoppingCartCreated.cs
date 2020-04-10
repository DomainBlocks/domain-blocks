using System;

namespace Shopping.Domain.Events
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