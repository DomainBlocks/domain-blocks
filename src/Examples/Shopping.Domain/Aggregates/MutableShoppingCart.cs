using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;
using DomainBlocks.Aggregates.Builders;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Domain.Aggregates;

public class MutableShoppingCart
{
    private readonly IEventDispatcher<IDomainEvent> _eventDispatcher;
    private readonly List<ShoppingCartItem> _items = new();

    public MutableShoppingCart(IEventDispatcher<IDomainEvent> eventDispatcher)
    {
        _eventDispatcher = eventDispatcher;
    }
    
    public Guid? Id { get; private set; }
    public IReadOnlyList<ShoppingCartItem> Items => _items.AsReadOnly();

    public static void RegisterEvents(EventRegistryBuilder<MutableShoppingCart, IDomainEvent> events)
    {
        events
            .Event<ShoppingCartCreated>().RoutesTo((agg, e) => agg.Apply(e))
            .Event<ItemAddedToShoppingCart>().RoutesTo((agg, e) => agg.Apply(e))
            .Event<ItemRemovedFromShoppingCart>().RoutesTo((agg, e) => agg.Apply(e));
    }

    public void Execute(AddItemToShoppingCart command)
    {
        var isNew = Id == null;
        if (isNew)
        {
            _eventDispatcher.Dispatch(this, new ShoppingCartCreated(command.CartId));
        }

        _eventDispatcher.Dispatch(this, new ItemAddedToShoppingCart(command.Id, command.CartId, command.Item));
    }

    public void Execute(RemoveItemFromShoppingCart command)
    {
        if (Items.All(i => i.Id != command.Id))
        {
            throw new InvalidOperationException("Item not in shopping cart");
        }

        _eventDispatcher.Dispatch(this, new ItemRemovedFromShoppingCart(command.Id, command.CartId));
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