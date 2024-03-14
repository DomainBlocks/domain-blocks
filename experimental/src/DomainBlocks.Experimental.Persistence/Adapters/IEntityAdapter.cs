namespace DomainBlocks.Experimental.Persistence.Adapters;

public interface IEntityAdapter
{
    Type EntityType { get; }
}

public interface IEntityAdapter<TEntity, TState>
{
    // Required for writes
    string GetId(TEntity entity);
    TState GetCurrentState(TEntity entity);
    IEnumerable<object> GetRaisedEvents(TEntity entity);

    // Required for reads
    TState CreateState();
    TState Fold(TState state, object @event);
    TEntity Create(TState state);
}

public interface IEntityAdapter<TEntity> : IEntityAdapter<TEntity, TEntity>
{
    TEntity IEntityAdapter<TEntity, TEntity>.GetCurrentState(TEntity entity) => entity;
    TEntity IEntityAdapter<TEntity, TEntity>.Create(TEntity state) => state;
}