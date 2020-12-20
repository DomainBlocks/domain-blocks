using System;
using System.Collections.Generic;
using System.Linq;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Domain.Aggregates
{
    public class MutableShoppingCart
    {
        private readonly List<ShoppingCartItem> _items = new List<ShoppingCartItem>();
        
        public Guid? Id { get; private set; }
        public IReadOnlyList<ShoppingCartItem> Items => _items.AsReadOnly();
        
        public IEnumerable<IDomainEvent> Execute(AddItemToShoppingCart command)
        {
            var isNew = Id == null;

            if (isNew)
            {
                yield return new ShoppingCartCreated(command.CartId);
            }

            yield return new ItemAddedToShoppingCart(command.Id, command.CartId, command.Item);
        }

        public IEnumerable<IDomainEvent> Execute(RemoveItemFromShoppingCart command)
        {
            if (Items.All(i => i.Id != command.Id))
            {
                throw new InvalidOperationException("Item not in shopping cart");
            }

            yield return new ItemRemovedFromShoppingCart(command.Id, command.CartId);
        }

        public void Apply(ShoppingCartCreated @event)
        {
            Id = @event.Id;
        }

        public void Apply(ItemAddedToShoppingCart @event)
        {
            if (Id != @event.CartId)
            {
                throw new InvalidOperationException("Attempted to add an item for a shopping cart with a different ID");
            }

            _items.Add(new ShoppingCartItem(@event.Id, @event.Item));
        }

        public void Apply(ItemRemovedFromShoppingCart @event)
        {
            if (Id != @event.CartId)
            {
                throw new InvalidOperationException("Attempted to remove an item for a shopping cart with a different ID");
            }

            var index = _items.FindIndex(item => item.Id == @event.Id);
            _items.RemoveAt(index);
        }
    }
}