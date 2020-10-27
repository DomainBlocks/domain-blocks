using System;
using DomainLib.Aggregates;

namespace Shopping.Domain.Events
{
    [EventName("ItemAddedToShoppingCart")]
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