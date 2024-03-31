using DomainBlocks.Abstractions;
using DomainBlocks.Persistence.Builders;
using DomainBlocks.Persistence.SqlStreamStore;
using DomainBlocks.Playground;
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

var services = new ServiceCollection();
services.AddDbContext<ShoppingCartDbContext>(
    options => options.UseNpgsql(
        "Server=shopping-db;Port=5432;Database=shopping-read;User Id=postgres;Password=postgres;"),
    optionsLifetime: ServiceLifetime.Singleton);

services.AddDbContextFactory<ShoppingCartDbContext>();

var serviceProvider = services.BuildServiceProvider();

var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>();

var eventMapperBuilder = new EventMapperBuilder();
eventMapperBuilder.MapAll<IDomainEvent>();
var eventMapper = eventMapperBuilder.Build();
var readModel = new ShoppingCartReadModel(dbContextFactory, eventMapper);

var continueAfterPosition = await readModel.OnLoadCheckpointAsync(CancellationToken.None);
var subscription = eventStore.SubscribeToAll(continueAfterPosition);

await using var messageEnumerator = subscription.ReadMessagesAsync().GetAsyncEnumerator();

var nextMessageTask = messageEnumerator.MoveNextAsync().AsTask();
var tickTask = Task.Delay(TimeSpan.FromSeconds(1));

while (true)
{
    var completedTask = await Task.WhenAny(nextMessageTask, tickTask);

    if (completedTask == nextMessageTask)
    {
        if (nextMessageTask.Result)
        {
            var message = messageEnumerator.Current;

            switch (message)
            {
                case StreamMessage.Event e:
                    await readModel.OnEvent(e.ReadEvent, CancellationToken.None);
                    break;
                case StreamMessage.CaughtUp:
                    await readModel.OnCaughtUp(CancellationToken.None);
                    break;
                case StreamMessage.FellBehind:
                    await readModel.OnFellBehind(CancellationToken.None);
                    break;
                case StreamMessage.SubscriptionDropped dropped:
                    await readModel.OnSubscriptionDropped(CancellationToken.None);
                    break;
            }

            nextMessageTask = messageEnumerator.MoveNextAsync().AsTask();
        }
    }
    else
    {
        Console.WriteLine("Tick");
        tickTask = Task.Delay(TimeSpan.FromSeconds(1));
    }
}