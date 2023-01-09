using System;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Core.Builders;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Serialization.EventStore;
using DomainBlocks.EventStore.Testing;
using DomainBlocks.Persistence;
using DomainBlocks.Persistence.EventStore;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Infrastructure.Tests;

[TestFixture]
public class ShoppingCartInfrastructureTests : EventStoreIntegrationTest
{
    [Test]
    public async Task PersistedRoundTripTest()
    {
        var model = new ModelBuilder()
            .ImmutableAggregate<ShoppingCartState, IDomainEvent>(aggregate =>
            {
                aggregate.WithKeyPrefix("shoppingCart");
                aggregate.ApplyEventsWith(ShoppingCartFunctions.Apply);
                aggregate.UseEventTypesFrom(typeof(IDomainEvent).Assembly);
            })
            .Build();

        var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.

        var serializer = new JsonBytesEventDataSerializer();
        var eventAdapter = new EventStoreEventAdapter(serializer);
        var eventConverter = EventConverter.Create(model.EventNameMap, eventAdapter);
        var eventsRepository = new EventStoreEventsRepository(EventStoreClient, eventConverter);
        var snapshotRepository = new EventStoreSnapshotRepository(EventStoreClient, eventConverter);

        var aggregateRepository = new AggregateRepository(eventsRepository, snapshotRepository, model);
        var loadedAggregate = await aggregateRepository.LoadAsync<ShoppingCartState>(shoppingCartId.ToString());

        // Execute the first command.
        var command1 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "First Item");
        loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command1));

        // Execute the second command to the result of the first command.
        var command2 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "Second Item");
        loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command2));

        Assert.That(loadedAggregate.State.Id.HasValue, "Expected ShoppingCart ID to be set");

        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        var nextEventVersion = await aggregateRepository.SaveAsync(loadedAggregate);
        var expectedNextEventVersion = eventsToPersist.Count - 1;

        Assert.That(nextEventVersion, Is.EqualTo(expectedNextEventVersion));

        loadedAggregate = await aggregateRepository.LoadAsync<ShoppingCartState>(shoppingCartId.ToString());
        var loadedState = loadedAggregate.State;
        var loadedVersion = loadedAggregate.Version;

        // Check the loaded aggregate root state.
        Assert.That(loadedVersion, Is.EqualTo(2));
        Assert.That(loadedState.Id, Is.EqualTo(shoppingCartId));
        Assert.That(loadedState.Items, Has.Count.EqualTo(2));
        Assert.That(loadedState.Items[0].Name, Is.EqualTo("First Item"));
        Assert.That(loadedState.Items[1].Name, Is.EqualTo("Second Item"));
    }
}