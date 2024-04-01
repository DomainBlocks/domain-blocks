using DomainBlocks.V1.Subscriptions;
using MongoDB.Driver;
using Shopping.Domain.Events;

namespace DomainBlocks.V1.Playground;

public class MongoShoppingCartProjection : ReadModelProjectionBase<IMongoCollection<MongoShoppingCartSummaryItem>>
{
    private readonly IMongoCollection<MongoShoppingCartSummaryItem> _collection;

    public MongoShoppingCartProjection()
    {
        var client = new MongoClient("mongodb://admin:password@localhost:27017");
        var database = client.GetDatabase("test");
        _collection = database.GetCollection<MongoShoppingCartSummaryItem>("users");

        When<ItemAddedToShoppingCart>(async (v, e, ct) =>
        {
            var result = await _collection
                .Find(x => x.SessionId == e.SessionId)
                .FirstOrDefaultAsync(cancellationToken: ct);

            if (result != null)
            {
                return;
            }

            await v.InsertOneAsync(
                new MongoShoppingCartSummaryItem
                {
                    SessionId = e.SessionId,
                    Item = e.Item
                },
                cancellationToken: ct);
        });
    }

    public override Task<IMongoCollection<MongoShoppingCartSummaryItem>> GetViewAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_collection);
    }
}