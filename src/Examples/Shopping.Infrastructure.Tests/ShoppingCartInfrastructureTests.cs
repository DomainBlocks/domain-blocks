using System;
using System.Linq;
using System.Threading.Tasks;
using DomainLib.Aggregates;
using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Serialization;
using EventStore.ClientAPI;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace Shopping.Infrastructure.Tests
{
    [TestFixture]
    public class ShoppingCartInfrastructureTests : EmbeddedEventStoreTest
    {
        [Test]
        public async Task PersistedRoundTripTest()
        {
            var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.

            // Execute the first command.
            var initialState = new ShoppingCart();
            var command1 = new AddItemToShoppingCart(shoppingCartId, "First Item");
            var result1 = initialState.Execute(command1);

            // Execute the second command to the result of the first command.
            var command2 = new AddItemToShoppingCart(shoppingCartId, "Second Item");
            var result2 = result1.NewState.Execute(command2);

            Assert.That(result2.NewState.Id.HasValue, "Expected ShoppingCart ID to be set");

            var eventsToPersist = result1.AppliedEvents.Concat(result2.AppliedEvents).ToList();

            var serializer = CreateJsonSerializer(initialState.EventNameMap);
            var repository = new EventStoreEventsRepository(EventStoreConnection, serializer);

            var streamName = $"shoppingCart-{result2.NewState.Id.Value}";

            var nextEventVersion = await repository.SaveEventsAsync(streamName, ExpectedVersion.NoStream, eventsToPersist);
            var expectedNextEventVersion = eventsToPersist.Count - 1;

            Assert.That(nextEventVersion, Is.EqualTo(expectedNextEventVersion));

            var eventsFromPersistence = (await repository.LoadEventsAsync<IDomainEvent>(streamName));
            var loadedAggregate = ShoppingCart.FromEvents(eventsFromPersistence);

            // Check the loaded aggregate root state.
            Assert.That(loadedAggregate.Id, Is.EqualTo(shoppingCartId));
            Assert.That(loadedAggregate.Items, Has.Count.EqualTo(2));
            Assert.That(loadedAggregate.Items[0], Is.EqualTo("First Item"));
            Assert.That(loadedAggregate.Items[1], Is.EqualTo("Second Item"));
        }

        private static IEventSerializer CreateJsonSerializer(IEventNameMap eventNameMap)
        {
            var serializer = new JsonEventSerializer();

            serializer.RegisterEventTypeMappings(eventNameMap);

            return serializer;
        }

    }
}