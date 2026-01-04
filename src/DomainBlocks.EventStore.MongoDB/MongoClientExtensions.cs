using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoClientExtensions
{
    public static IMongoCollection<TDocument> GetCollection<TDocument>(
        this IMongoClient client,
        CollectionNamespace collectionNamespace)
    {
        var db = client.GetDatabase(collectionNamespace.DatabaseNamespace.DatabaseName);
        return db.GetCollection<TDocument>(collectionNamespace.CollectionName);
    }
}