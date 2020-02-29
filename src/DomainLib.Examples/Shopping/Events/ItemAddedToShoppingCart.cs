using System;

namespace DomainLib.Examples.Shopping.Events
{
    public class ItemAddedToShoppingCart : IDomainEvent
    {
        public ItemAddedToShoppingCart(Guid id, string item)
        {
            Id = id;
            Item = item;
        }

        public Guid Id { get; }
        public string Item { get; }
    }
}