using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.V1.Playground;

public class MongoShoppingCartSummaryItem
{
    [BsonId] public Guid SessionId { get; set; }
    public string? Item { get; set; }
}