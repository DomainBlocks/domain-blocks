using DomainBlocks.V1.Abstractions.Subscriptions;
using DomainBlocks.V1.Subscriptions;
using MongoDB.Driver;
using Shopping.Domain.Events;

namespace DomainBlocks.V1.Playground;

public class MongoShoppingCartProjection : IEventStreamConsumer, IEventHandler<ItemAddedToShoppingCart>
{
    private readonly IMongoCollection<MongoShoppingCart> _collection;

    public MongoShoppingCartProjection()
    {
        var client = new MongoClient("mongodb://admin:password@localhost:27017");
        var database = client.GetDatabase("test");
        _collection = database.GetCollection<MongoShoppingCart>("shopping-carts");
    }

    public async Task OnEventAsync(EventHandlerContext<ItemAddedToShoppingCart> context)
    {
        var cart = await _collection
            .Find(x => x.SessionId == context.Event.SessionId)
            .FirstOrDefaultAsync(context.CancellationToken) ?? new MongoShoppingCart
        {
            SessionId = context.Event.SessionId
        };

        if (!cart.Items.Contains(context.Event.Item))
        {
            cart.Items.Add(context.Event.Item);
        }

        await _collection.ReplaceOneAsync(
            filter: x => x.SessionId == context.Event.SessionId,
            replacement: cart,
            options: new ReplaceOptions { IsUpsert = true },
            cancellationToken: context.CancellationToken);
    }
}