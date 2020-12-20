using System;

namespace Shopping.Domain.Commands
{
    public class AddItemToShoppingCart
    {
        public AddItemToShoppingCart(Guid cartId, Guid id, string item)
        {
            CartId = cartId;
            Id = id;
            Item = item;
        }

        public Guid CartId { get; }
        public Guid Id { get; }
        public string Item { get; }
    }
}