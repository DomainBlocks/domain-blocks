using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using System;
using System.Linq;
using DomainLib.Aggregates.Registration;

namespace Shopping.Domain.Tests
{
    [TestFixture]
    public class ImmutableShoppingCartTests
    {
        [Test]
        public void RoundTripTest()
        {
            var aggregateRegistryBuilder = AggregateRegistryBuilder.Create<object, IDomainEvent>();
            ShoppingCartFunctions.Register(aggregateRegistryBuilder);
            
            var aggregateRegistry = aggregateRegistryBuilder.Build();
            var commandDispatcher = aggregateRegistry.CommandDispatcher;
            var eventDispatcher = aggregateRegistry.EventDispatcher;

            var initialState = new ShoppingCartState();
            
            // Execute the first command.
            var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.
            var command1 = new AddItemToShoppingCart(shoppingCartId, "First Item");
            var (newState1, events1) = commandDispatcher.ImmutableDispatch(initialState, command1);
            
            // Check the updated aggregate root state.
            Assert.That(newState1.Id, Is.EqualTo(shoppingCartId));
            Assert.That(newState1.Items, Has.Count.EqualTo(1));
            Assert.That(newState1.Items[0], Is.EqualTo("First Item"));
            
            // We expect that two events were be applied.
            Assert.That(events1, Has.Count.EqualTo(2));
            Assert.That(events1[0], Is.TypeOf<ShoppingCartCreated>());
            Assert.That(events1[1], Is.TypeOf<ItemAddedToShoppingCart>());

            // Execute the second command to the result of the first command.
            var command2 = new AddItemToShoppingCart(shoppingCartId, "Second Item");
            var (newState2, events2)  = commandDispatcher.ImmutableDispatch(newState1, command2);
            
            // Check the updated aggregate root state.
            Assert.That(newState2.Id, Is.EqualTo(shoppingCartId));
            Assert.That(newState2.Items, Has.Count.EqualTo(2));
            Assert.That(newState2.Items[0], Is.EqualTo("First Item"));
            Assert.That(newState2.Items[1], Is.EqualTo("Second Item"));
            
            // We expect that one event was applied.
            Assert.That(events2, Has.Count.EqualTo(1));
            Assert.That(events2[0], Is.TypeOf<ItemAddedToShoppingCart>());

            // The aggregate event log is the sum total of all events from both commands.
            // Simulate loading from the event log.
            var eventLog = events1.Concat(events2);
            var loadedAggregate = ShoppingCartState.FromEvents(eventDispatcher, eventLog);
            
            // Check the loaded aggregate root state.
            Assert.That(loadedAggregate.Id, Is.EqualTo(shoppingCartId));
            Assert.That(loadedAggregate.Items, Has.Count.EqualTo(2));
            Assert.That(loadedAggregate.Items[0], Is.EqualTo("First Item"));
            Assert.That(loadedAggregate.Items[1], Is.EqualTo("Second Item"));
        }
    }
}
