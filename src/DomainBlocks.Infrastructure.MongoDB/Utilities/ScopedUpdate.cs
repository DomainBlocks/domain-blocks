using System.Linq.Expressions;
using MongoDB.Bson;

namespace DomainBlocks.Infrastructure.MongoDB.Utilities;

public static class ScopedUpdate
{
    public static ScopedUpdate<TRootDocument> For<TRootDocument>() => new();
}

public sealed class ScopedUpdate<TRootDocument>
{
    public ScopedUpdateBuilder<TRootDocument, TSubDocument> At<TSubDocument>(
        Expression<Func<TRootDocument, TSubDocument>> field)
    {
        var rootPath = FieldPathResolver.Resolve(field);
        return new ScopedUpdateBuilder<TRootDocument, TSubDocument>(rootPath);
    }

    public ScopedUpdateBuilder<TRootDocument> At(
        Expression<Func<TRootDocument, BsonDocument>> field)
    {
        var rootPath = FieldPathResolver.Resolve(field);
        return new ScopedUpdateBuilder<TRootDocument>(rootPath);
    }
}