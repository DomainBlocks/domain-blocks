﻿using System;
using System.Linq;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Domain.Tests
{ 
    [TestFixture]
    public class ShoppingCartTests
    {
        [Test]
        public void RoundTripTest()
        {
            var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.

            // Execute the first command.
            var initialState = new ShoppingCart();
            var command1 = new AddItemToShoppingCart(shoppingCartId, "First Item");
            var result1 = initialState.Execute(command1);
            
            // Check the updated aggregate root state.
            Assert.That(result1.NewState.Id, Is.EqualTo(shoppingCartId));
            Assert.That(result1.NewState.Items, Has.Count.EqualTo(1));
            Assert.That(result1.NewState.Items[0], Is.EqualTo("First Item"));
            
            // We expect that two events were be applied.
            Assert.That(result1.AppliedEvents, Has.Count.EqualTo(2));
            Assert.That(result1.AppliedEvents[0], Is.TypeOf<ShoppingCartCreated>());
            Assert.That(result1.AppliedEvents[1], Is.TypeOf<ItemAddedToShoppingCart>());

            // Execute the second command to the result of the first command.
            var command2 = new AddItemToShoppingCart(shoppingCartId, "Second Item");
            var result2 = result1.NewState.Execute(command2);
            
            // Check the updated aggregate root state.
            Assert.That(result2.NewState.Id, Is.EqualTo(shoppingCartId));
            Assert.That(result2.NewState.Items, Has.Count.EqualTo(2));
            Assert.That(result2.NewState.Items[0], Is.EqualTo("First Item"));
            Assert.That(result2.NewState.Items[1], Is.EqualTo("Second Item"));
            
            // We expect that one event was applied.
            Assert.That(result2.AppliedEvents, Has.Count.EqualTo(1));
            Assert.That(result2.AppliedEvents[0], Is.TypeOf<ItemAddedToShoppingCart>());

            // The aggregate event log is the sum total of all events from both commands.
            // Simulate loading from the event log.
            var eventLog = result1.AppliedEvents.Concat(result2.AppliedEvents);
            var loadedAggregate = ShoppingCart.FromEvents(eventLog);
            
            // Check the loaded aggregate root state.
            Assert.That(loadedAggregate.Id, Is.EqualTo(shoppingCartId));
            Assert.That(loadedAggregate.Items, Has.Count.EqualTo(2));
            Assert.That(loadedAggregate.Items[0], Is.EqualTo("First Item"));
            Assert.That(loadedAggregate.Items[1], Is.EqualTo("Second Item"));
        }
    }
}
