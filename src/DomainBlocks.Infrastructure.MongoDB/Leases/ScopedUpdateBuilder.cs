using System.Linq.Expressions;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public class ScopedUpdateBuilder<TRootDocument>
{
    private readonly string _rootPath;

    internal ScopedUpdateBuilder(string rootPath)
    {
        _rootPath = rootPath;
    }

    public ScopedUpdateBuilder<TRootDocument, TDocument> As<TDocument>()
    {
        return new ScopedUpdateBuilder<TRootDocument, TDocument>(_rootPath);
    }
}

public class ScopedUpdateBuilder<TRootDocument, TSubDocument> : IScopedUpdateBuilder<TSubDocument>
{
    private readonly string _rootPath;
    private readonly List<UpdateDefinition<TRootDocument>> _updates = [];

    internal ScopedUpdateBuilder(string rootPath)
    {
        _rootPath = rootPath;
    }

    public IScopedUpdateBuilder<TSubDocument> Inc<TField>(
        Expression<Func<TSubDocument, TField>> field,
        TField value)
    {
        _updates.Add(Builders<TRootDocument>.Update.Inc(GetFieldPath(field), value));
        return this;
    }

    public IScopedUpdateBuilder<TSubDocument> Set<TField>(
        Expression<Func<TSubDocument, TField>> field,
        TField value)
    {
        _updates.Add(Builders<TRootDocument>.Update.Set(GetFieldPath(field), value));
        return this;
    }

    internal UpdateDefinition<TRootDocument> Build() => Builders<TRootDocument>.Update.Combine(_updates);

    private string GetFieldPath<TField>(Expression<Func<TSubDocument, TField>> field)
    {
        return $"{_rootPath}.{MongoFieldPathResolver.Resolve(field)}";
    }
}