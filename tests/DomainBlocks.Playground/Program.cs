using DomainBlocks.Abstractions;
using DomainBlocks.Persistence.Builders;
using DomainBlocks.Persistence.SqlStreamStore;
using DomainBlocks.Playground;
using DomainBlocks.Subscriptions;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Domain.Events;
using Shopping.ReadModel;

var settings = new PostgresStreamStoreSettings(
    "Server=localhost;Port=5434;Database=test-events;User Id=postgres;Password=postgres;");

var streamStore = new PostgresStreamStore(settings);
await streamStore.CreateSchemaIfNotExists();

var eventStore = new SqlStreamStoreEventStore(streamStore);

// Builder concept
// var subscriber = new StreamSubscriberBuilder()
//     .UseSqlStreamStore(streamStore)
//     .SubscribeToAll()
//     .AddConsumer<ShoppingCartReadModel>(config =>
//     {
//         config.UseCheckpointPolicy<MyCheckpointPolicy>();
//         config.UseRetryPolicy<MyRetryPolicy>();
//     })
//     .Build();

var services = new ServiceCollection();

services.AddDbContext<ShoppingCartDbContext>(
    options => options.UseNpgsql(
        "Server=shopping-db;Port=5432;Database=shopping-read;User Id=postgres;Password=postgres;"),
    optionsLifetime: ServiceLifetime.Singleton);

services.AddDbContextFactory<ShoppingCartDbContext>();

var serviceProvider = services.BuildServiceProvider();

var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>();

var eventMapper = new EventMapperBuilder().MapAll<IDomainEvent>(_ => { }).Build();
var readModel = new ShoppingCartReadModel(dbContextFactory, eventMapper);

var subscriber = new EventStreamSubscriber(
    pos => eventStore.SubscribeToAll(pos),
    new[] { readModel });

subscriber.Start();
await subscriber.WaitForCompletedAsync();