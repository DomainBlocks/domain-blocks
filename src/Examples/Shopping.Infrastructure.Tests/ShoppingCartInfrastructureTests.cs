using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Serialization;
using EventStore.ClientAPI;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using System;
using System.Linq;
using System.Threading.Tasks;
using DomainLib.Aggregates;
using DomainLib.Aggregates.Registration;

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
            
            var command1 = new AddItemToShoppingCart(shoppingCartId, "First Item");
            var result1 = aggregateRegistry.CommandDispatcher.Dispatch(initialState, command1);

            // Execute the second command to the result of the first command.
            var command2 = new AddItemToShoppingCart(shoppingCartId, "Second Item");
            var result2 = aggregateRegistry.CommandDispatcher.Dispatch(result1.NewState, command2);

            Assert.That(result2.NewState.Id.HasValue, "Expected ShoppingCart ID to be set");

            var eventsToPersist = result1.AppliedEvents.Concat(result2.AppliedEvents).ToList();

            var serializer = new JsonEventSerializer(aggregateRegistry.EventNameMap);
            var eventsRepository = new EventStoreEventsRepository(EventStoreConnection, serializer);
            var snapshotRepository = new EventStoreSnapshotRepository(EventStoreConnection, serializer);

            var aggregateRepository = new AggregateRepository<IDomainEvent>(eventsRepository, 
                                                                            snapshotRepository, 
                                                                            aggregateRegistry.EventDispatcher,
                                                                            aggregateRegistry.AggregateMetadataMap);

            var nextEventVersion = await aggregateRepository.SaveAggregate<ShoppingCartState>(result2.NewState.Id.ToString(),
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
            Assert.That(loadedState.Items[0], Is.EqualTo("First Item"));
            Assert.That(loadedState.Items[1], Is.EqualTo("Second Item"));
        }
    }
}