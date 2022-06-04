using System;
using System.Collections.Generic;
using System.Linq;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Domain.Aggregates;

using EventApplier = Action<MutableShoppingCart, IDomainEvent>;

public class MutableShoppingCart
{
    private readonly List<ShoppingCartItem> _items = new();
    
    public Guid? Id { get; private set; }
    public IReadOnlyList<ShoppingCartItem> Items => _items.AsReadOnly();

    public static EventApplier EventApplier => (state, @event) => state.Apply((dynamic)@event);

    public void Execute(AddItemToShoppingCart command, EventApplier eventApplier)
    {
        var isNew = Id == null;
        if (isNew)
        {
            eventApplier(this, new ShoppingCartCreated(command.CartId));
        }

        eventApplier(this, new ItemAddedToShoppingCart(command.Id, command.CartId, command.Item));
    }

    public void Execute(RemoveItemFromShoppingCart command, EventApplier eventApplier)
    {
        if (Items.All(i => i.Id != command.Id))
        {
            throw new InvalidOperationException("Item not in shopping cart");
        }

        eventApplier(this, new ItemRemovedFromShoppingCart(command.Id, command.CartId));
    }

    private void Apply(ShoppingCartCreated @event)
    {
        Id = @event.Id;
    }

    private void Apply(ItemAddedToShoppingCart @event)
    {
        if (Id != @event.CartId)
        {
            throw new InvalidOperationException("Attempted to add an item for a shopping cart with a different ID");
        }

        _items.Add(new ShoppingCartItem(@event.Id, @event.Item));
    }

    private void Apply(ItemRemovedFromShoppingCart @event)
    {
        if (Id != @event.CartId)
        {
            throw new InvalidOperationException("Attempted to remove an item for a shopping cart with a different ID");
        }

        var index = _items.FindIndex(item => item.Id == @event.Id);
        _items.RemoveAt(index);
    }
}