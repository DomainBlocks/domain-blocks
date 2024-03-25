namespace DomainBlocks.Experimental.Persistence.Entities;

public interface IEntityAdapter
{
    Type EntityType { get; }
    Type StateType { get; }
}

public interface IEntityAdapter<TEntity> : IEntityAdapter
{
    Type IEntityAdapter.EntityType => typeof(TEntity);

    // Required for writes
    string GetId(TEntity entity);
    object GetCurrentState(TEntity entity);
    IEnumerable<object> GetRaisedEvents(TEntity entity);

    // Required for reads
    object CreateState();

    Task<TEntity> RestoreEntityAsync(
        object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken);
}