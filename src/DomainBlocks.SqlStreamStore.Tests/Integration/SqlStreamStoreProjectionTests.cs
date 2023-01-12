using DomainBlocks.Core.Persistence;
using DomainBlocks.Core.Projections.Experimental.Builders;
using DomainBlocks.Core.Subscriptions;
using DomainBlocks.DependencyInjection;
using DomainBlocks.SqlStreamStore.Persistence;
using DomainBlocks.SqlStreamStore.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using SqlStreamStore;

namespace DomainBlocks.SqlStreamStore.Tests.Integration;

[TestFixture]
public class SqlStreamStoreProjectionTests
{
    [Test]
    [Timeout(1000)]
    public async Task WriteReadWithCheckpointTest()
    {
        var streamStore = new InMemoryStreamStore();

        // Setup write model
        var writeServices = new ServiceCollection();

        writeServices.AddAggregateRepository((options, model) =>
        {
            options.UseSqlStreamStore(o => o.WithInstance(streamStore));

            model.Aggregate<MutableShoppingCart, IDomainEvent>(aggregate =>
            {
                aggregate.WithRaisedEventsFrom(x => x.RaisedEvents);
                aggregate.ApplyEventsWith((agg, e) => agg.ApplyEvent(e));
                aggregate.UseEventTypesFrom(typeof(IDomainEvent).Assembly);
            });
        });

        var writeServiceProvider = writeServices.BuildServiceProvider();
        var aggregateRepository = writeServiceProvider.GetRequiredService<IAggregateRepository>();
        var command = new AddItemToShoppingCart(Guid.NewGuid(), Guid.NewGuid(), "Item 1");
        var shoppingCart = await aggregateRepository.LoadAsync<MutableShoppingCart>(command.CartId.ToString());
        shoppingCart.ExecuteCommand(x => x.Execute(command));
        await aggregateRepository.SaveAsync(shoppingCart);

        // Setup read model
        var readServices = new ServiceCollection();
        var itemsByCart = new Dictionary<Guid, List<string>>();
        long? lastPosition = null;
        var checkpointSignal = new TaskCompletionSource();
        var exception = new Exception("Event handler error");

        readServices.AddEventStreamSubscription((_, options) =>
        {
            options
                .UseSqlStreamStore(o => o.WithInstance(streamStore))
                .FromAllEventsStream()
                .ProjectTo()
                .SingletonState(itemsByCart)
                .WithCheckpoints(x => x.PerEventCount(2))
                .OnCheckpoint((_, pos, _) =>
                {
                    lastPosition = pos;
                    checkpointSignal.SetResult();
                    return Task.CompletedTask;
                })
                .OnEventError((_, _, _) => Task.FromResult(EventErrorResolution.Retry))
                .Map(x => x.EventName("ItemAddedToShoppingCart").ToType<ItemAddedToShoppingCart2>())
                .When<ShoppingCartCreated>((e, s) =>
                {
                    try
                    {
                        if (exception != null) throw exception;
                        s.Add(e.Id, new List<string>());
                    }
                    finally
                    {
                        exception = null;
                    }
                })
                .When<ItemAddedToShoppingCart2>((e, s) => s[e.CartId].Add(e.Item));
        });

        var readServiceProvider = readServices.BuildServiceProvider();
        var subscription = readServiceProvider.GetRequiredService<IEventStreamSubscription>();

        var task = await Task.WhenAny(
            subscription.StartAsync(),
            subscription.WaitForCompletedAsync(),
            checkpointSignal.Task);

        Assert.That(task.Status, Is.Not.EqualTo(TaskStatus.Faulted), () => task.Exception!.Message);
        Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        Assert.That(itemsByCart, Contains.Key(command.CartId));
        Assert.That(itemsByCart[command.CartId], Is.EqualTo(new[] { command.Item }));
        Assert.That(lastPosition, Is.Not.Null);
        Assert.That(lastPosition, Is.EqualTo(1));
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private class ItemAddedToShoppingCart2
    {
        public ItemAddedToShoppingCart2(Guid cartId, string item)
        {
            CartId = cartId;
            Item = item;
        }

        public Guid CartId { get; }
        public string Item { get; }
    }
}

// Idea for process managers:
//
// writeServices.AddProcessRepository((options, model) =>
// {
//     options.UseSqlStreamStore(o => o.WithInstance(streamStore));
//
//     model.Process<MyProcess, IDomainEvent, IDomainCommand>(process =>
//     {
//         process.WithRaisedCommandsFrom(x => x.NewCommands);
//         process.ApplyEventsWith((proc, e) => proc.Transition(e));
//         process.UseEventTypesFrom(typeof(IDomainEvent).Assembly);
//     });
// });
//
// readServices.AddEventStreamSubscription((sp, options) =>
// {
//     options
//         .UseSqlStreamStore(o => o.WithInstance(streamStore))
//         .FromAllEventsStream()
//         .ProjectTo()
//         .Process<MyProcess>()
//         .WithProcessRepository(() => sp.GetRequiredService<IProcessRepository>())
//         .On<DoSomething>(command =>
//         {
//             // Do something with the command, e.g. dispatch to message bus, invoke service, etc.
//         });
// });