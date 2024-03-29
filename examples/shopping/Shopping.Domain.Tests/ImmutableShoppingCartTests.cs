﻿using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using System;
using System.Linq;

namespace Shopping.Domain.Tests;

[TestFixture]
public class ImmutableShoppingCartTests
{
    [Test]
    public void RoundTripTest()
    {
        var initialState = new ShoppingCartState();

        // Execute the first command.
        var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.
        var command1 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "First Item");
        var events1 = ShoppingCartFunctions.Execute(initialState, command1).ToList();
        var newState1 = events1.Aggregate(initialState, ShoppingCartFunctions.Apply);

        // Check the updated aggregate root state.
        Assert.That(newState1.Id, Is.EqualTo(shoppingCartId));
        Assert.That(newState1.Items, Has.Count.EqualTo(1));
        Assert.That(newState1.Items[0].Name, Is.EqualTo("First Item"));

        // We expect that two events were be applied.
        Assert.That(events1, Has.Count.EqualTo(2));
        Assert.That(events1[0], Is.TypeOf<ShoppingCartCreated>());
        Assert.That(events1[1], Is.TypeOf<ItemAddedToShoppingCart>());

        // Execute the second command to the result of the first command.
        var command2 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "Second Item");
        var events2 = ShoppingCartFunctions.Execute(newState1, command2).ToList();
        var newState2 = events2.Aggregate(newState1, ShoppingCartFunctions.Apply);

        // Check the updated aggregate root state.
        Assert.That(newState2.Id, Is.EqualTo(shoppingCartId));
        Assert.That(newState2.Items, Has.Count.EqualTo(2));
        Assert.That(newState2.Items[0].Name, Is.EqualTo("First Item"));
        Assert.That(newState2.Items[1].Name, Is.EqualTo("Second Item"));

        // We expect that one event was applied.
        Assert.That(events2, Has.Count.EqualTo(1));
        Assert.That(events2[0], Is.TypeOf<ItemAddedToShoppingCart>());

        // The aggregate event log is the sum total of all events from both commands.
        // Simulate loading from the event log.
        var eventLog = events1.Concat(events2);
        var loadedAggregate = eventLog.Aggregate(new ShoppingCartState(), ShoppingCartFunctions.Apply);

        // Check the loaded aggregate root state.
        Assert.That(loadedAggregate.Id, Is.EqualTo(shoppingCartId));
        Assert.That(loadedAggregate.Items, Has.Count.EqualTo(2));
        Assert.That(loadedAggregate.Items[0].Name, Is.EqualTo("First Item"));
        Assert.That(loadedAggregate.Items[1].Name, Is.EqualTo("Second Item"));
    }
}