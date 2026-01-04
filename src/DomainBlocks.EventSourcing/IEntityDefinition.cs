namespace DomainBlocks.EventSourcing;

// ReSharper disable once UnusedTypeParameter - marker interface
public interface IEntityDefinition<TEvent> where TEvent : notnull
{
    Type EntityType { get; }
}

public interface IEntityDefinition<TEvent, TEntity> : IEntityDefinition<TEvent>
    where TEvent : notnull
    where TEntity : notnull
{
    Type IEntityDefinition<TEvent>.EntityType => typeof(TEntity);

    // Not used yet, but will be used for snapshot deserialization.
    Type StateType { get; }

    string GetId(TEntity entity);

    // Not used yet, but will be used for snapshot serialization.
    object GetState(TEntity entity);

    IEnumerable<TEvent> GetUncommittedEvents(TEntity entity);

    object CreateInitialState();

    Task<TEntity> RestoreAsync(
        object initialState,
        IAsyncEnumerable<TEvent> events,
        CancellationToken cancellationToken);
}