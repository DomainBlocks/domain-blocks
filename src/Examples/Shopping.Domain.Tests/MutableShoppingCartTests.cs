using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Domain.Tests;

[TestFixture]
public class MutableShoppingCartTests
{
    [Test]
    public void RoundTripTest()
    {
        // Initial state
        var shoppingCart = new MutableShoppingCart();

        // Simple wrapper around EventApplier to capture applied events.
        var appliedEvents = new List<IDomainEvent>();
        void ApplyEvent(MutableShoppingCart state, IDomainEvent @event)
        {
            MutableShoppingCart.EventApplier(state, @event);
            appliedEvents.Add(@event);
        }

        // Execute the first command.
        var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.
        var command1 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "First Item");
        shoppingCart.Execute(command1, ApplyEvent);
        var events1 = appliedEvents.ToList();
        appliedEvents.Clear();

        // Check the updated aggregate root state.
        Assert.That(shoppingCart.Id, Is.EqualTo(shoppingCartId));
        Assert.That(shoppingCart.Items, Has.Count.EqualTo(1));
        Assert.That(shoppingCart.Items[0].Name, Is.EqualTo("First Item"));

        // We expect that two events were be applied.
        Assert.That(events1, Has.Count.EqualTo(2));
        Assert.That(events1[0], Is.TypeOf<ShoppingCartCreated>());
        Assert.That(events1[1], Is.TypeOf<ItemAddedToShoppingCart>());

        // Execute the second command to the result of the first command.
        var command2 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "Second Item");
        shoppingCart.Execute(command2, ApplyEvent);
        var events2 = appliedEvents.ToList();
        appliedEvents.Clear();

        // Check the updated aggregate root state.
        Assert.That(shoppingCart.Id, Is.EqualTo(shoppingCartId));
        Assert.That(shoppingCart.Items, Has.Count.EqualTo(2));
        Assert.That(shoppingCart.Items[0].Name, Is.EqualTo("First Item"));
        Assert.That(shoppingCart.Items[1].Name, Is.EqualTo("Second Item"));

        // We expect that one event was applied.
        Assert.That(events2, Has.Count.EqualTo(1));
        Assert.That(events2[0], Is.TypeOf<ItemAddedToShoppingCart>());

        // The aggregate event log is the sum total of all events from both commands.
        // Simulate loading from the event log.
        var eventLog = events1.Concat(events2);
        var aggregateFromEvents = eventLog.Aggregate(new MutableShoppingCart(), (s, e) =>
        {
            MutableShoppingCart.EventApplier(s, e);
            return s;
        });

        // Check the loaded aggregate root state.
        Assert.That(aggregateFromEvents.Id, Is.EqualTo(shoppingCartId));
        Assert.That(aggregateFromEvents.Items, Has.Count.EqualTo(2));
        Assert.That(aggregateFromEvents.Items[0].Name, Is.EqualTo("First Item"));
        Assert.That(aggregateFromEvents.Items[1].Name, Is.EqualTo("Second Item"));
    }
}