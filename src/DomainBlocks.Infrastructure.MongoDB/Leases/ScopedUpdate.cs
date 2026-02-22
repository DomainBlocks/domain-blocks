using System.Linq.Expressions;
using MongoDB.Bson;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public static class ScopedUpdate
{
    public static ScopedUpdateBuilder<TRootDocument, TSubDocument> At<TRootDocument, TSubDocument>(
        Expression<Func<TRootDocument, BsonDocument>> field)
    {
        var rootPath = MongoFieldPathResolver.Resolve(field);
        return new ScopedUpdateBuilder<TRootDocument, TSubDocument>(rootPath);
    }
}