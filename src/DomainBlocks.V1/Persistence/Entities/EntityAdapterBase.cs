namespace DomainBlocks.V1.Persistence.Entities;

public abstract class EntityAdapterBase<TEntity, TState> : IEntityAdapter<TEntity>
    where TEntity : notnull
    where TState : notnull
{
    public Type StateType => typeof(TState);

    // Required for writes
    public abstract string GetId(TEntity entity);
    public abstract TState GetCurrentState(TEntity entity);
    public abstract IEnumerable<object> GetRaisedEvents(TEntity entity);

    // Required for reads
    public abstract TState CreateState();
    protected abstract TState Fold(TState state, object @event);
    protected abstract TEntity Create(TState state);

    object IEntityAdapter<TEntity>.GetCurrentState(TEntity entity) => GetCurrentState(entity);
    object IEntityAdapter<TEntity>.CreateState() => CreateState();

    async Task<TEntity> IEntityAdapter<TEntity>.RestoreEntityAsync(
        object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken)
    {
        var currentState = (TState)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            currentState = Fold(currentState, e);
        }

        return Create(currentState);
    }
}

public abstract class EntityAdapterBase<TEntity> : EntityAdapterBase<TEntity, TEntity> where TEntity : notnull
{
    public override TEntity GetCurrentState(TEntity entity) => entity;
    protected override TEntity Create(TEntity state) => state;
}