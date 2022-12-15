using DomainBlocks.Persistence;
using DomainBlocks.Persistence.DependencyInjection;
using DomainBlocks.Persistence.SqlStreamStore;
using DomainBlocks.Projections;
using DomainBlocks.Projections.DependencyInjection;
using DomainBlocks.Projections.SqlStreamStore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;
using SqlStreamStore;

namespace DomainBlocks.IntegrationTests.SqlStreamStore;

[TestFixture]
public class SqlStreamStoreProjectionTests
{
    [Test]
    public async Task WriteReadWithBookMarkTest()
    {
        var streamStore = new InMemoryStreamStore();

        // Setup write model
        var writeServices = new ServiceCollection();

        writeServices.AddAggregateRepository((options, model) =>
        {
            options.UseSqlStreamStore(o => o.UseInMemoryStreamStore(streamStore));

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
        IStreamPosition? lastPosition = null;
        var signal = new SemaphoreSlim(0, 1);

        readServices.AddEventCatchUpSubscription((_, options, model) =>
        {
            options.UseSqlStreamStore(o => o.UseInMemoryStreamStore(streamStore));

            model.Projection<Dictionary<Guid, List<string>>>(projection =>
            {
                projection.WithStateFactory(_ => itemsByCart);

                projection.OnSave((_, pos, _) =>
                {
                    lastPosition = pos;
                    signal.Release();
                    return Task.CompletedTask;
                });

                projection.When<ItemAddedToShoppingCart>((e, s) =>
                {
                    if (!s.TryGetValue(e.CartId, out var cartItems))
                    {
                        cartItems = new List<string>();
                        s.Add(e.CartId, cartItems);
                    }

                    cartItems.Add(e.Item);
                });
            });
        });

        var readServiceProvider = readServices.BuildServiceProvider();
        var eventDispatcher = readServiceProvider.GetRequiredService<IEventDispatcher>();
        await eventDispatcher.StartAsync();
        await signal.WaitAsync();

        Assert.That(itemsByCart, Contains.Key(command.CartId));
        Assert.That(itemsByCart[command.CartId], Is.EqualTo(new[] { command.Item }));
        Assert.That(lastPosition, Is.Not.Null);
        Assert.That(lastPosition!.ToJsonString(), Is.EqualTo("{\"Position\":1}"));
    }
}