using System;
using System.Collections.Generic;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Domain.Aggregates
{
    public class MutableShoppingCart
    {
        private readonly List<string> _items = new List<string>();
        
        public Guid? Id { get; private set; }
        public IReadOnlyList<string> Items => _items.AsReadOnly();
        
        public IEnumerable<IDomainEvent> Execute(AddItemToShoppingCart command)
        {
            var isNew = Id == null;

            if (isNew)
            {
                yield return new ShoppingCartCreated(command.Id);
            }

            yield return new ItemAddedToShoppingCart(command.Id, command.Item);
        }

        public void Apply(ShoppingCartCreated @event)
        {
            Id = @event.Id;
        }

        public void Apply(ItemAddedToShoppingCart @event)
        {
            if (Id != @event.Id)
            {
                throw new InvalidOperationException("Attempted to add an item for a shopping cart with a different ID");
            }

            _items.Add(@event.Item);
        }
    }
}