using System;

namespace Shopping.Domain.Commands
{
    public class RemoveItemFromShoppingCart
    {
        public RemoveItemFromShoppingCart(Guid id, Guid cartId)
        {
            Id = id;
            CartId = cartId;
        }

        public Guid Id { get; }
        public Guid CartId { get; }
    }
}