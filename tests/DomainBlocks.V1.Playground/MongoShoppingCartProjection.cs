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
                .FirstOrDefaultAsync(cancellationToken: ct);

            // TODO
        });
    }
}