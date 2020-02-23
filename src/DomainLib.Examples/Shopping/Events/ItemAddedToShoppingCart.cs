using System;

namespace DomainLib.Examples.Shopping.Events
{
    public class ItemAddedToShoppingCart : IDomainEvent
    {
        public ItemAddedToShoppingCart(Guid id, string item)
        {
            Item = item;
        }
        
        public string Item { get; }
    }
}