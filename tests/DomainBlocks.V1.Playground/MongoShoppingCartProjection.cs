using DomainBlocks.V1.Subscriptions;
using MongoDB.Driver;
using Shopping.Domain.Events;

namespace DomainBlocks.V1.Playground;

public class MongoShoppingCartProjection : CatchUpSubscriptionConsumerBase
{
    public MongoShoppingCartProjection()
    {
        var client = new MongoClient("mongodb://admin:password@localhost:27017");
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<MongoShoppingCart>("users");

        When<ItemAddedToShoppingCart>(async (e, ct) =>
        {
            var cart = await collection
                .Find(x => x.SessionId == e.SessionId)
                .FirstOrDefaultAsync(cancellationToken: ct) ?? new MongoShoppingCart
            {
                SessionId = e.SessionId
            };

            if (!cart.Items.Contains(e.Item))
            {
                cart.Items.Add(e.Item);
            }

            await collection.ReplaceOneAsync(
                filter: x => x.SessionId == e.SessionId,
                options: new ReplaceOptions { IsUpsert = true },
                replacement: cart,
                cancellationToken: ct);
        });
    }
}