using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventSourcing;

public interface IEntityAdapterProvider
{
    bool TryGetFor<TEntity>([NotNullWhen(true)] out IEntityAdapter<TEntity>? entityAdapter) where TEntity : notnull;
}