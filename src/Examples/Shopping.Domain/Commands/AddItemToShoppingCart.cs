using System;

namespace Shopping.Domain.Commands
{
    public class AddItemToShoppingCart
    {
        public AddItemToShoppingCart(Guid id, string item)
        {
            Id = id;
            Item = item;
        }

        public Guid Id { get; }
        public string Item { get; }
    }
}