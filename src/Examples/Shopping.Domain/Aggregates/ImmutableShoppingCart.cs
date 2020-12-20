using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using DomainLib.Aggregates;
using DomainLib.Aggregates.Registration;

namespace Shopping.Domain.Aggregates
{
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

        public Guid? Id { get;  }
        public IReadOnlyList<ShoppingCartItem> Items { get; }

        public static ShoppingCartState FromEvents(EventDispatcher<IDomainEvent> eventDispatcher,
                                                   IEnumerable<IDomainEvent> events) =>
            eventDispatcher.ImmutableDispatch(new ShoppingCartState(), events);
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
        public static void Register(AggregateRegistryBuilder<object, IDomainEvent> aggregateRegistryBuilder)
        {
            aggregateRegistryBuilder.Register<ShoppingCartState>(aggregate =>
            {
                aggregate.Id(o => o.Id?.ToString())
                         .PersistenceKey(id => $"shoppingCart-{id}")
                         .SnapshotKey(id => $"shoppingCartSnapshot-{id}");

                aggregate.Command<AddItemToShoppingCart>()
                         .RoutesTo(Execute);

                aggregate.Command<RemoveItemFromShoppingCart>()
                         .RoutesTo(Execute);

                aggregate.Event<ShoppingCartCreated>()
                         .RoutesTo(Apply)
                         .HasName(ShoppingCartCreated.EventName);

                aggregate.Event<ItemAddedToShoppingCart>()
                         .RoutesTo(Apply)
                         .HasName(ItemAddedToShoppingCart.EventName);

                aggregate.Event<ItemRemovedFromShoppingCart>()
                         .RoutesTo(Apply)
                         .HasName(ItemRemovedFromShoppingCart.EventName);
            });
        }

        private static IEnumerable<IDomainEvent> Execute(Func<ShoppingCartState> getState, AddItemToShoppingCart command)
        {
            var isNew = getState().Id == null;

            if (isNew)
            {
                yield return new ShoppingCartCreated(command.CartId);
            }

            yield return new ItemAddedToShoppingCart(command.Id, command.CartId, command.Item);
        }

        private static IEnumerable<IDomainEvent> Execute(Func<ShoppingCartState> getState,
                                                         RemoveItemFromShoppingCart command)
        {
            var state = getState();

            if (state.Items.All(i => i.Id != command.Id))
            {
                throw new InvalidOperationException("Item not in shopping cart");
            }

            yield return new ItemRemovedFromShoppingCart(command.Id, command.CartId);
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

            var newItems = new List<ShoppingCartItem>(currentState.Items) {new ShoppingCartItem(@event.Id, @event.Item)};
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
}