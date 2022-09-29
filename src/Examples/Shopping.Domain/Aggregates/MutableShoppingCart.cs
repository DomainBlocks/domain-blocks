using System;
using System.Collections.Generic;
using System.Linq;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Domain.Aggregates;

public class MutableShoppingCart
{
    private readonly List<ShoppingCartItem> _items = new();
    private readonly List<IDomainEvent> _raisedEvents = new();
    
    public Guid? Id { get; private set; }
    public IReadOnlyList<ShoppingCartItem> Items => _items.AsReadOnly();
    public IReadOnlyList<IDomainEvent> RaisedEvents => _raisedEvents;

    public void Execute(AddItemToShoppingCart command)
    {
        var isNew = Id == null;
        if (isNew)
        {
            RaiseEvent(new ShoppingCartCreated(command.CartId));
        }
        
        RaiseEvent(new ItemAddedToShoppingCart(command.Id, command.CartId, command.Item));
    }

    public void Execute(RemoveItemFromShoppingCart command)
    {
        if (Items.All(i => i.Id != command.Id))
        {
            throw new InvalidOperationException("Item not in shopping cart");
        }

        RaiseEvent(new ItemRemovedFromShoppingCart(command.Id, command.CartId));
    }

    public void ApplyEvent(IDomainEvent @event)
    {
        switch (@event)
        {
            case ShoppingCartCreated e:
                Apply(e);
                break;
            case ItemAddedToShoppingCart e:
                Apply(e);
                break;
            case ItemRemovedFromShoppingCart e:
                Apply(e);
                break;
        }
    }

    private void RaiseEvent(IDomainEvent @event)
    {
        ApplyEvent(@event);
        _raisedEvents.Add(@event);
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