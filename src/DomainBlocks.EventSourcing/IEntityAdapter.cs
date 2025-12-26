namespace DomainBlocks.EventSourcing;

public interface IEntityAdapter
{
    Type EntityType { get; }
}

public interface IEntityAdapter<TEntity, TEventBase> : IEntityAdapter where TEntity : notnull where TEventBase : class
{
    Type IEntityAdapter.EntityType => typeof(TEntity);

    // Not used yet, but will be used for snapshot deserialization.
    Type StateType { get; }

    string GetId(TEntity entity);

    // Not used yet, but will be used for snapshot serialization.
    object GetCurrentState(TEntity entity);

    IEnumerable<TEventBase> GetUncommittedEvents(TEntity entity);

    object CreateState();

    Task<TEntity> RestoreAsync(
        object initialState,
        IAsyncEnumerable<TEventBase> events,
        CancellationToken cancellationToken);
}