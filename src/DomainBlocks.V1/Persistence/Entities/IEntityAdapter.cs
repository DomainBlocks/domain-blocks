namespace DomainBlocks.V1.Persistence.Entities;

public interface IEntityAdapter
{
    Type EntityType { get; }
}

public interface IEntityAdapter<TEntity> : IEntityAdapter where TEntity : notnull
{
    Type IEntityAdapter.EntityType => typeof(TEntity);

    // Not used yet, but will be used for snapshot deserialization.
    Type StateType { get; }

    string GetId(TEntity entity);

    // Not used yet, but will be used for snapshot serialization.
    object GetCurrentState(TEntity entity);

    IEnumerable<object> GetRaisedEvents(TEntity entity);

    object CreateState();

    Task<TEntity> RestoreEntityAsync(
        object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken);
}