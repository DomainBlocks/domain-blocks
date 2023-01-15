using DomainBlocks.Core.Projections.Experimental.Builders;
using DomainBlocks.Core.Subscriptions.Builders;
using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.SqlStreamStore.Postgres;
using DomainBlocks.SqlStreamStore.Subscriptions;
using EventStore.Client;
using Shopping.Domain.Events;

namespace Projections.Playground;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        //await TestLogicalReplication(args);
        await TestSqlStreamStoreAllEventsStreamSubscription();
        //await TestEventStoreAllEventsStreamSubscription();
    }

    // See https://github.com/oskardudycz/PostgresOutboxPatternWithCDC.NET
    // private static async Task TestLogicalReplication(string[] args)
    // {
    //     const string connectionString =
    //         "Server=localhost;Port=5433;Database=shopping;User Id=postgres;Password=postgres;";
    //
    //     var subscriptionOptions = new EventsSubscriptionOptions(connectionString, args[0], "messages_pub");
    //     var subscription = new EventsSubscription();
    //
    //     await foreach (var @event in subscription.Subscribe(subscriptionOptions, default))
    //     {
    //         Console.WriteLine(@event);
    //     }
    // }

    private static async Task TestSqlStreamStoreAllEventsStreamSubscription()
    {
        const string connectionString =
            "Server=localhost;Port=5433;Database=shopping;User Id=postgres;Password=postgres;";

        var subscription = new EventStreamSubscriptionBuilder()
            .UseSqlStreamStore(x => x.UsePostgresStreamStore(connectionString))
            .FromAllEventsStream()
            .ProjectTo()
            .State(status =>
            {
                Console.WriteLine("State created: {0}", status);
                return new object();
            })
            .WithCheckpoints(x => x.PerEventCount(1))
            .OnStarting((_, _) =>
            {
                Console.WriteLine("OnStarting");
                return Task.FromResult<long?>(null);
            })
            .OnCatchingUp((_, _) =>
            {
                Console.WriteLine("OnCatchingUp");
                return Task.CompletedTask;
            })
            .OnCheckpoint((_, pos, _) =>
            {
                Console.WriteLine("OnCheckpoint: {0}", pos);
                return Task.CompletedTask;
            })
            .OnLive((_, _) =>
            {
                Console.WriteLine("OnLive");
                return Task.CompletedTask;
            })
            .When<ShoppingCartCreated>((_, _) => Console.WriteLine("ShoppingCartCreated"))
            .When<ItemAddedToShoppingCart>((_, _) => Console.WriteLine("ItemAddedToShoppingCart"))
            .When<ItemRemovedFromShoppingCart>((_, _) => Console.WriteLine("ItemRemovedFromShoppingCart"))
            .Build();

        await subscription.StartAsync();
        await subscription.WaitForCompletedAsync();
    }

    private static async Task TestEventStoreAllEventsStreamSubscription()
    {
        const string connectionString = "esdb://127.0.0.1:2113?tls=false";

        var subscription = new EventStreamSubscriptionBuilder()
            .UseEventStore(connectionString)
            .FromAllEventsStream()
            .ProjectTo()
            .State(status =>
            {
                Console.WriteLine("State created: {0}", status);
                return new object();
            })
            .WithCheckpoints(x => x.PerEventCount(1))
            .OnStarting((_, _) =>
            {
                Console.WriteLine("OnStarting");
                return Task.FromResult<Position?>(null);
            })
            .OnCatchingUp((_, _) =>
            {
                Console.WriteLine("OnCatchingUp");
                return Task.CompletedTask;
            })
            .OnCheckpoint((_, pos, _) =>
            {
                Console.WriteLine("OnCheckpoint: {0}", pos);
                return Task.CompletedTask;
            })
            .OnLive((_, _) =>
            {
                Console.WriteLine("OnLive");
                return Task.CompletedTask;
            })
            .When<TestEvent>((_, _) => Console.WriteLine("TestEvent"))
            .Build();

        await subscription.StartAsync();
        await subscription.WaitForCompletedAsync();
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private class TestEvent
    {
    }
}