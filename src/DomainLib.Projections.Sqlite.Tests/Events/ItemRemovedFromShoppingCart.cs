using System;

namespace DomainLib.Projections.Sqlite.Tests.Events
{
    public class ItemRemovedFromShoppingCart
    {
        public const string EventName = "ItemRemovedFromShoppingCart";

        public ItemRemovedFromShoppingCart(Guid itemId, Guid cartId)
        {
            ItemId = itemId;
            CartId = cartId;
        }

        public Guid ItemId { get; }
        public Guid CartId { get; }
    }
}