using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shopping.Domain.Aggregates;

// Demonstrates immutable state, but it could equally be mutable.
// Note: the immutable implementation could be better. It's just for demo purposes.
public class ShoppingCartState
{
    public ShoppingCartState()
    {
    }

    public ShoppingCartState(Guid? id)
    {
        Id = id;
        Items = new List<ShoppingCartItem>();
    }

    public ShoppingCartState(Guid? id, IReadOnlyList<ShoppingCartItem> items)
    {
        Id = id;
        Items = items;
    }

    public Guid? Id { get; }
    public IReadOnlyList<ShoppingCartItem> Items { get; }
}

public class ShoppingCartItem
{
    public ShoppingCartItem(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; }
}
    
public static class ShoppingCartFunctions
{
    public static IEnumerable<IDomainEvent> Execute(ShoppingCartState state, AddItemToShoppingCart command)
    {
        var isNew = state.Id == null;
        if (isNew)
        {
            yield return new ShoppingCartCreated(command.CartId);
        }

        yield return new ItemAddedToShoppingCart(command.Id, command.CartId, command.Item);
    }

    public static IEnumerable<IDomainEvent> Execute(ShoppingCartState state, RemoveItemFromShoppingCart command)
    {
        if (state.Items.All(i => i.Id != command.Id))
        {
            throw new InvalidOperationException("Item not in shopping cart");
        }

        yield return new ItemRemovedFromShoppingCart(command.Id, command.CartId);
    }

    public static ShoppingCartState Apply(ShoppingCartState currentState, IDomainEvent @event)
    {
        return @event switch
        {
            ShoppingCartCreated e => Apply(currentState, e),
            ItemAddedToShoppingCart e => Apply(currentState, e),
            ItemRemovedFromShoppingCart e => Apply(currentState, e),
            // Simply ignore unknown events
            _ => currentState
        };
    }

    private static ShoppingCartState Apply(ShoppingCartState currentState, ShoppingCartCreated @event)
    {
        return new ShoppingCartState(@event.Id);
    }

    private static ShoppingCartState Apply(ShoppingCartState currentState, ItemAddedToShoppingCart @event)
    {
        if (currentState.Id != @event.CartId)
        {
            throw new InvalidOperationException("Attempted to add an item for a shopping cart with a different ID");
        }

        var newItems = new List<ShoppingCartItem>(currentState.Items) { new(@event.Id, @event.Item) };
        return new ShoppingCartState(currentState.Id, newItems);
    }

    private static ShoppingCartState Apply(ShoppingCartState currentState, ItemRemovedFromShoppingCart @event)
    {
        if (currentState.Id != @event.CartId)
        {
            throw new InvalidOperationException("Attempted to remove an item for a shopping cart with a different ID");
        }

        var newItems = currentState.Items.Where(i => i.Id != @event.Id).ToList();
        return new ShoppingCartState(currentState.Id, newItems);
    }
}