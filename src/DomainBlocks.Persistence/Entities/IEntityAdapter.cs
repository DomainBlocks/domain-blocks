namespace DomainBlocks.Persistence.Entities;

public interface IEntityAdapter
{
    Type EntityType { get; }
}

public interface IEntityAdapter<TEntity> : IEntityAdapter
{
    Type IEntityAdapter.EntityType => typeof(TEntity);
    Type StateType { get; }

    // Required for writes
    string GetId(TEntity entity);
    object GetCurrentState(TEntity entity);
    IEnumerable<object> GetRaisedEvents(TEntity entity);

    // Required for reads
    object CreateState();

    Task<TEntity> RestoreEntityAsync(
        object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken);
}