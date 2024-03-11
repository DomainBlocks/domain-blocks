namespace DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;

public delegate Task<TEntity> EntityRestorer<TEntity>(
    object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken);