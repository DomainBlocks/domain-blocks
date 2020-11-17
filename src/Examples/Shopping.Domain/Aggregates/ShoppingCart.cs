using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using System;
using System.Collections.Generic;
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
            Items = new List<string>();
        }

        public ShoppingCartState(Guid? id, IReadOnlyList<string> items)
        {
            Id = id;
            Items = items;
        }

        public Guid? Id { get;  }
        public IReadOnlyList<string> Items { get; }

        public static ShoppingCartState FromEvents(EventDispatcher<IDomainEvent> eventDispatcher,
                                                   IEnumerable<IDomainEvent> events) =>
            eventDispatcher.DispatchEvents(new ShoppingCartState(), events);
    }
    
    public static class ShoppingCartFunctions
    {
        public static void Register(AggregateRegistryBuilder<object, IDomainEvent> aggregateRegistryBuilder)
        {
            aggregateRegistryBuilder.Register<ShoppingCartState>(aggregate =>
            {
                aggregate.PersistenceKey(id => $"shoppingCart-{id}");
                aggregate.SnapshotKey(id => $"shoppingCartSnapshot-{id}");

                aggregate.Command<AddItemToShoppingCart>()
                         .RoutesTo(Execute);

                aggregate.Event<ShoppingCartCreated>()
                         .RoutesTo(Apply)
                         .HasName(ShoppingCartCreated.EventName);

                aggregate.Event<ItemAddedToShoppingCart>()
                         .RoutesTo(Apply)
                         .HasName(ItemAddedToShoppingCart.EventName);
            });
        }

        private static IEnumerable<IDomainEvent> Execute(Func<ShoppingCartState> getState, AddItemToShoppingCart command)
        {
            var isNew = getState().Id == null;

            if (isNew)
            {
                yield return new ShoppingCartCreated(command.Id);
            }

            yield return new ItemAddedToShoppingCart(command.Id, command.Item);
        }

        private static ShoppingCartState Apply(ShoppingCartState currentState, ShoppingCartCreated @event)
        {
            return new ShoppingCartState(@event.Id);
        }

        private static ShoppingCartState Apply(ShoppingCartState currentState, ItemAddedToShoppingCart @event)
        {
            if (currentState.Id != @event.Id)
            {
                throw new InvalidOperationException("Attempted to add an item for a shopping cart with a different ID");
            }

            var newItems = new List<string>(currentState.Items) { @event.Item };
            return new ShoppingCartState(currentState.Id, newItems);
        }
    }
}