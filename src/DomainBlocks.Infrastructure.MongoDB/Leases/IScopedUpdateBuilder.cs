using System.Linq.Expressions;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public interface IScopedUpdateBuilder<TDocument>
{
    IScopedUpdateBuilder<TDocument> Inc<TField>(Expression<Func<TDocument, TField>> field, TField value);

    IScopedUpdateBuilder<TDocument> Set<TField>(Expression<Func<TDocument, TField>> field, TField value);
}