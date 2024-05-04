using DomainBlocks.V1.EventStoreDb.Extensions;
using DomainBlocks.V1.Persistence;
using DomainBlocks.V1.Persistence.Builders;
using Shopping.Domain.Events;
using Shopping.WriteModel;

namespace DomainBlocks.V1.Tests.Integration;

[TestFixture]
public class WriteTests
{
    [Test]
    public async Task WriteLotsOfEvents()
    {
        var config = new EntityStoreConfigBuilder()
            .UseEventStoreDb("esdb://localhost:2113?tls=false")
            .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(AggregateAdapter<>)))
            .MapEvents(x => x.MapAll<IDomainEvent>())
            .Build();

        var store = new EntityStore(config);
        var entity = new Shopping.Domain.ShoppingCart();
        entity.StartSession();

        foreach (var i in Enumerable.Range(1, 100))
        {
            entity.AddItem($"Item {i}");
        }

        await store.SaveAsync(entity);
    }
}