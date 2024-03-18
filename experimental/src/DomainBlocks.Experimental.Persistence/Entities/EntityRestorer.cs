namespace DomainBlocks.Experimental.Persistence.Entities;

public delegate Task<TEntity> EntityRestorer<TEntity>(
    object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken);