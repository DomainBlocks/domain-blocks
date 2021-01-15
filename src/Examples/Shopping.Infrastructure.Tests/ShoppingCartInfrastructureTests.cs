using DomainLib.Aggregates.Registration;
using DomainLib.EventStore.Testing;
using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Serialization.Json;
using EventStore.ClientAPI;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Shopping.Infrastructure.Tests
{
    [TestFixture]
    public class ShoppingCartInfrastructureTests : EmbeddedEventStoreTest
    {
       [Test]
        public async Task PersistedRoundTripTest()
        {
            var registryBuilder = AggregateRegistryBuilder.Create<object, IDomainEvent>();
            ShoppingCartFunctions.Register(registryBuilder);
            var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.

            var aggregateRegistry = registryBuilder.Build();
            
            // Execute the first command.
            var initialState = new ShoppingCartState();
            
            var command1 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "First Item");
            var (newState1, events1) = aggregateRegistry.CommandDispatcher.ImmutableDispatch(initialState, command1);

            // Execute the second command to the result of the first command.
            var command2 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "Second Item");
            var (newState2, events2) = aggregateRegistry.CommandDispatcher.ImmutableDispatch(newState1, command2);

            Assert.That(newState2.Id.HasValue, "Expected ShoppingCart ID to be set");

            var eventsToPersist = events1.Concat(events2).ToList();

            var serializer = new JsonBytesEventSerializer(aggregateRegistry.EventNameMap);
            var eventsRepository = new EventStoreEventsRepository(EventStoreConnection, serializer);
            var snapshotRepository = new EventStoreSnapshotRepository(EventStoreConnection, serializer);

            var aggregateRepository = AggregateRepository.Create(eventsRepository,
                                                                 snapshotRepository,
                                                                 aggregateRegistry.EventDispatcher,
                                                                 aggregateRegistry.AggregateMetadataMap);

            var nextEventVersion = await aggregateRepository.SaveAggregate<ShoppingCartState>(newState2.Id.ToString(),
                                                                                              ExpectedVersion.NoStream,
                                                                                              eventsToPersist);
            var expectedNextEventVersion = eventsToPersist.Count - 1;

            Assert.That(nextEventVersion, Is.EqualTo(expectedNextEventVersion));

            var loadedData = await aggregateRepository.LoadAggregate(shoppingCartId.ToString(), new ShoppingCartState());

            var loadedState = loadedData.AggregateState;
            var loadedVersion = loadedData.Version;

            // Check the loaded aggregate root state.
            Assert.That(loadedVersion, Is.EqualTo(2));
            Assert.That(loadedState.Id, Is.EqualTo(shoppingCartId));
            Assert.That(loadedState.Items, Has.Count.EqualTo(2));
            Assert.That(loadedState.Items[0].Name, Is.EqualTo("First Item"));
            Assert.That(loadedState.Items[1].Name, Is.EqualTo("Second Item"));
        }
    }
}