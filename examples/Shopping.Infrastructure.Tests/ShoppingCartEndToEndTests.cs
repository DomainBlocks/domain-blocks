using System;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Core;
using DomainBlocks.Core.Builders;
using DomainBlocks.Core.Persistence;
using DomainBlocks.Core.Serialization;
using DomainBlocks.EventStore.Persistence;
using DomainBlocks.EventStore.Projections;
using DomainBlocks.EventStore.Serialization;
using DomainBlocks.EventStore.Testing;
using DomainBlocks.Logging;
using EventStore.Client;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using ProjectionDispatcher = DomainBlocks.Core.Projections.EventDispatcher<EventStore.Client.EventRecord>;
using UserCredentials = DomainBlocks.Core.UserCredentials;

namespace Shopping.Infrastructure.Tests;

// TODO (DS): These tests are pretty much broken. We need to give EventStore support some TLC.
// To be addressed in a future PR.
[TestFixture]
public class ShoppingCartEndToEndTests : EventStoreIntegrationTest
{
    [Test]
    [Ignore("Need to figure out a better end to end test")]
    public async Task ReadModelIsBuilt()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddConsole());
        Log.SetLoggerFactory(loggerFactory);

        await SetUpReadModelProjections();
        await SetUpProcessManagerProjections();

        await WriteEventsToStream();

        await Task.Delay(200);
    }

    private async Task SetUpReadModelProjections()
    {
        // TODO (DS): We need some projections here!

        //var projectionRegistryBuilder = new ProjectionRegistryBuilder();

        //var registry = projectionRegistryBuilder.Build();

        var readModelEventPublisher = new EventStoreEventPublisher(EventStoreClient);

        // var readModelDispatcher = new ProjectionDispatcher(
        //     readModelEventPublisher,
        //     registry.EventProjectionMap,
        //     registry.ProjectionContextMap,
        //     new EventRecordJsonDeserializer(),
        //     registry.EventNameMap,
        //     EventDispatcherConfiguration.ReadModelDefaults);
        //
        // await readModelDispatcher.StartAsync();

        await Task.CompletedTask;
    }

    private async Task SetUpProcessManagerProjections()
    {
        var credentials = new UserCredentials("admin", "changeit");

        var settings = new PersistentSubscriptionSettings(startFrom: StreamPosition.End);

        var stream = "ShoppingCartProcess";
        var groupName = "SubscriptionGroup1";

        await PersistentSubscriptionsClient.CreateAsync(stream, groupName, settings, credentials.ToEsUserCredentials());

        //var processProjectionRegistryBuilder = new ProjectionRegistryBuilder();

        //ShoppingCartProcess.Register(processProjectionRegistryBuilder);
        //var processRegistry = processProjectionRegistryBuilder.Build();

        var persistentSubscriptionDescriptor =
            new EventStorePersistentConnectionDescriptor(stream, groupName, 10, credentials);

        var processEventPublisher =
            new AcknowledgingEventStoreEventPublisher(PersistentSubscriptionsClient, persistentSubscriptionDescriptor);

        // var processDispatcher = new ProjectionDispatcher(processEventPublisher,
        //     processRegistry.EventProjectionMap,
        //     processRegistry.ProjectionContextMap,
        //     new EventRecordJsonDeserializer(),
        //     processRegistry.EventNameMap,
        //     EventDispatcherConfiguration.ReadModelDefaults);
    }

    private async Task WriteEventsToStream()
    {
        // TODO: Copied from ShoppingCartInfrastructureTests for the moment.
        // We should refactor this to allow better sharing
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

        // Execute the first command.
        var loadedAggregate = await aggregateRepository.LoadAsync<ShoppingCartState>(shoppingCartId.ToString());
        var command1 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "First Item");
        loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command1));

        // Execute the second command to the result of the first command.
        var secondItemId = Guid.NewGuid();
        var command2 = new AddItemToShoppingCart(shoppingCartId, secondItemId, "Second Item");
        loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command2));

        Assert.That(loadedAggregate.State.Id.HasValue, "Expected ShoppingCart ID to be set");

        var command3 = new RemoveItemFromShoppingCart(secondItemId, shoppingCartId);
        loadedAggregate.ExecuteCommand(x => ShoppingCartFunctions.Execute(x, command3));

        var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

        var nextEventVersion = await aggregateRepository.SaveAsync(loadedAggregate);
        var expectedNextEventVersion = eventsToPersist.Count - 1;

        Assert.That(nextEventVersion, Is.EqualTo(expectedNextEventVersion));
    }
}

// public static class ShoppingCartProcess
// {
//     public static void Register(ProjectionRegistryBuilder builder)
//     {
//         builder.Event<ItemRemovedFromShoppingCart>();
//     }
// }