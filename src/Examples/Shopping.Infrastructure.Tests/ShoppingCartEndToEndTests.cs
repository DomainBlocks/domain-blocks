using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Aggregates.Registration;
using DomainBlocks.Common;
using DomainBlocks.EventStore.Testing;
using DomainBlocks.Persistence;
using DomainBlocks.Persistence.EventStore;
using DomainBlocks.Projections;
using DomainBlocks.Projections.EventStore;
using DomainBlocks.Projections.Sql;
using DomainBlocks.Projections.Sqlite;
using DomainBlocks.Serialization.Json;
using EventStore.Client;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using ProjectionDispatcher = DomainBlocks.Projections.EventDispatcher<System.ReadOnlyMemory<byte>, Shopping.Domain.Events.IDomainEvent>;
using UserCredentials = DomainBlocks.Common.UserCredentials;

namespace Shopping.Infrastructure.Tests
{
    [TestFixture]
    public class ShoppingCartEndToEndTests : EventStoreIntegrationTest
    {
        [Test]
        [Ignore("Need to figure out a better end to end test")]
        public async Task ReadModelIsBuilt()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug)
                                                                       .AddConsole());

            Logger.SetLoggerFactory(loggerFactory);
            
            await SetUpReadModelProjections();
            await SetUpProcessManagerProjections();

            await WriteEventsToStream();

            await Task.Delay(200);
        }

        private async Task SetUpReadModelProjections()
        {
            var projectionRegistryBuilder = new ProjectionRegistryBuilder();

            ShoppingCartSummarySqlProjection.Register(projectionRegistryBuilder);

            var registry = projectionRegistryBuilder.Build();

            var readModelEventPublisher = new EventStoreEventPublisher(EventStoreClient);

            var readModelDispatcher = new ProjectionDispatcher(readModelEventPublisher,
                                                               registry.EventProjectionMap,
                                                               registry.ProjectionContextMap,
                                                               new JsonEventDeserializer(),
                                                               registry.EventNameMap,
                                                               EventDispatcherConfiguration.ReadModelDefaults);


            await readModelDispatcher.StartAsync();
        }

        private async Task SetUpProcessManagerProjections()
        {
            var credentials = new UserCredentials("admin", "changeit");

            var settings = new PersistentSubscriptionSettings(startFrom: StreamPosition.End);

            var stream = "ShoppingCartProcess";
            var groupName = "SubscriptionGroup1";
            
            await PersistentSubscriptionsClient.CreateAsync(stream, groupName, settings, credentials.ToEsUserCredentials());
            
            
            
            var processProjectionRegistryBuilder = new ProjectionRegistryBuilder();

            ShoppingCartProcess.Register(processProjectionRegistryBuilder);
            var processRegistry = processProjectionRegistryBuilder.Build();
            
            var persistentSubscriptionDescriptor = new EventStorePersistentConnectionDescriptor(stream, groupName, 10, credentials);
            var processEventPublisher = new AcknowledgingEventStoreEventPublisher(PersistentSubscriptionsClient, persistentSubscriptionDescriptor);

            var processDispatcher = new ProjectionDispatcher(processEventPublisher,
                                                             processRegistry.EventProjectionMap,
                                                             processRegistry.ProjectionContextMap,
                                                             new JsonEventDeserializer(),
                                                             processRegistry.EventNameMap,
                                                             EventDispatcherConfiguration.ReadModelDefaults);
        }

        private async Task WriteEventsToStream()
        {
            // TODO: Copied from ShoppingCartInfrastructureTests for the moment.
            // We should refactor this to allow better sharing

            var registryBuilder = AggregateRegistryBuilder.Create<object, IDomainEvent>();
            ShoppingCartFunctions.Register(registryBuilder);
            var shoppingCartId = Guid.NewGuid(); // This could come from a sequence, or could be the customer's ID.

            var aggregateRegistry = registryBuilder.Build();

            var serializer = new JsonBytesEventSerializer(aggregateRegistry.EventNameMap);
            var eventsRepository = new EventStoreEventsRepository(EventStoreClient, serializer);
            var snapshotRepository = new EventStoreSnapshotRepository(EventStoreClient, serializer);

            var aggregateRepository = AggregateRepository.Create(eventsRepository,
                                                                 snapshotRepository,
                                                                 aggregateRegistry);

            // Execute the first command.
            var loadedAggregate = await aggregateRepository.LoadAggregate<ShoppingCartState>(shoppingCartId.ToString());

            var command1 = new AddItemToShoppingCart(shoppingCartId, Guid.NewGuid(), "First Item");
            loadedAggregate.ImmutableDispatchCommand(command1);

            // Execute the second command to the result of the first command.
            var secondItemId = Guid.NewGuid();
            var command2 = new AddItemToShoppingCart(shoppingCartId, secondItemId, "Second Item");
            loadedAggregate.ImmutableDispatchCommand(command2);

            Assert.That(loadedAggregate.AggregateState.Id.HasValue, "Expected ShoppingCart ID to be set");

            var command3 = new RemoveItemFromShoppingCart(secondItemId, shoppingCartId);
            loadedAggregate.ImmutableDispatchCommand(command3);

            var eventsToPersist = loadedAggregate.EventsToPersist.ToList();

            var nextEventVersion = await aggregateRepository.SaveAggregate(loadedAggregate);
            var expectedNextEventVersion = eventsToPersist.Count - 1;

            Assert.That(nextEventVersion, Is.EqualTo(expectedNextEventVersion));
        }
    }

    public class ShoppingCartSummarySqlProjection : ISqlProjection
    {
        public static void Register(ProjectionRegistryBuilder builder)
        {
            var shoppingCartSummary = new ShoppingCartSummarySqlProjection();

            builder.Event<ItemAddedToShoppingCart>()
                   .FromName(ItemAddedToShoppingCart.EventName)
                   .ToSqlProjection(shoppingCartSummary)
                   .ExecutesUpsert();

            builder.Event<ItemRemovedFromShoppingCart>()
                   .FromName(ItemRemovedFromShoppingCart.EventName)
                   .ToSqlProjection(shoppingCartSummary)
                   .ExecutesDelete();
        }

        public IDbConnector DbConnector { get; } = new SqliteDbConnector("Data Source=test.db; Version=3;Pooling=True;Max Pool Size=100;");
        public ISqlDialect SqlDialect { get; } = new SqliteSqlDialect();
        public string TableName { get; } = "ShoppingCartSummary";

        public SqlColumnDefinitions Columns { get; } = new()
        {
            {nameof(ItemAddedToShoppingCart.Id), new SqlColumnDefinitionBuilder().Name("Id").Type(DbType.String).PrimaryKey().Build()},
            {nameof(ItemAddedToShoppingCart.CartId), new SqlColumnDefinitionBuilder().Name("CartId").Type(DbType.String).Build() },
            {nameof(ItemAddedToShoppingCart.Item), new SqlColumnDefinitionBuilder().Name("Item").Type(DbType.String).Build() },
        };
    }


    public class ShoppingCartProcess
    {
        public static void Register(ProjectionRegistryBuilder builder)
        {
            builder.Event<ItemRemovedFromShoppingCart>()
                   .FromName(ItemRemovedFromShoppingCart.EventName);
        }
    }


}
