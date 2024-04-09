using DomainBlocks.V1.SqlStreamStore;
using DomainBlocks.V1.Playground;
using DomainBlocks.V1.Subscriptions;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using DomainBlocks.V1.Persistence.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Domain.Events;
using Shopping.ReadModel;
using Shopping.ReadModel.Db;
using Shopping.ReadModel.Projections;

// Persistent subscriptions test
// var eventStoreDbSettings = EventStoreClientSettings.Create("esdb://localhost:2114?tls=false");
// var client = new EventStorePersistentSubscriptionsClient(eventStoreDbSettings);
//
// await client.CreateToAllAsync(
//     "test-all-group",
//     EventTypeFilter.ExcludeSystemEvents(),
//     new PersistentSubscriptionSettings());
//
// var result = client.SubscribeToAll("test-all-group");
//
// await foreach (var e in result)
// {
//     Console.WriteLine(e.Event.EventType);
//     await result.Ack(e);
// }

var settings = new PostgresStreamStoreSettings(
    "Server=localhost;Port=5434;Database=test-events;User Id=postgres;Password=postgres;");

var streamStore = new PostgresStreamStore(settings);
await streamStore.CreateSchemaIfNotExists();

var eventStore = new SqlStreamStoreEventStore(streamStore);

// Builder idea
// var config = new EventStreamSubscriptionConfigBuilder()
//     .UseSqlStreamStore(streamStore)
//     .SubscribeToAll()
//     .WithConsumer<PostgresShoppingCartProjection>()
//     .Build();

var services = new ServiceCollection();

services.AddDbContext<ShoppingCartDbContext>(
    options => options.UseNpgsql(
        "Server=localhost;Port=5434;Database=shopping-read;User Id=postgres;Password=postgres;"),
    optionsLifetime: ServiceLifetime.Singleton);

services.AddDbContextFactory<ShoppingCartDbContext>();

var serviceProvider = services.BuildServiceProvider();

var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>();
var consumer = new ShoppingCartProjection(dbContextFactory);
//var consumer = new MongoShoppingCartProjection();
var eventMapper = new EventMapperBuilder().MapAll<IDomainEvent>(_ => { }).Build();

var subscriber = new EventStreamSubscriptionService(
    "all-events",
    pos => eventStore.SubscribeToAll(pos?.AsGlobalPosition()),
    new[] { consumer },
    eventMapper);

subscriber.Start();
await subscriber.WaitForCompletedAsync();