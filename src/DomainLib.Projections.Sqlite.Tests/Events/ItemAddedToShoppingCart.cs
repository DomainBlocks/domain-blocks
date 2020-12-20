using System;

namespace DomainLib.Projections.Sqlite.Tests.Events
{
    public class ItemAddedToShoppingCart
    {
        public const string EventName = "ItemAddedToShoppingCart";

        public ItemAddedToShoppingCart(Guid cartId, Guid itemId, string itemDescription, decimal itemCost)
        {
            CartId = cartId;
            ItemId = itemId;
            ItemDescription = itemDescription;
            ItemCost = itemCost;
        }

        public Guid CartId { get; }
        public Guid ItemId { get; }
        public string ItemDescription { get; }
        public decimal ItemCost { get; }
    }
}