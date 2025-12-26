namespace DomainBlocks.EventSourcing;

// ReSharper disable once UnusedTypeParameter - marker interface
public interface IEntityAdapter<TEventBase> where TEventBase : class
{
    Type EntityType { get; }
}

public interface IEntityAdapter<TEventBase, TEntity> : IEntityAdapter<TEventBase>
    where TEventBase : class
    where TEntity : notnull
{
    Type IEntityAdapter<TEventBase>.EntityType => typeof(TEntity);

    // Not used yet, but will be used for snapshot deserialization.
    Type StateType { get; }

    string GetId(TEntity entity);

    // Not used yet, but will be used for snapshot serialization.
    object GetState(TEntity entity);

    IEnumerable<TEventBase> GetUncommittedEvents(TEntity entity);

    object CreateInitialState();

    Task<TEntity> RestoreAsync(
        object initialState,
        IAsyncEnumerable<TEventBase> events,
        CancellationToken cancellationToken);
}