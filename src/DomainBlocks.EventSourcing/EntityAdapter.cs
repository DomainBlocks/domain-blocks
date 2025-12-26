namespace DomainBlocks.EventSourcing;

public abstract class EntityAdapter<TEntity, TEventBase, TState> : IEntityAdapter<TEntity, TEventBase>
    where TEntity : notnull
    where TEventBase : class
    where TState : notnull
{
    public Type StateType => typeof(TState);

    // Required for writes
    public abstract string GetId(TEntity entity);
    public abstract TState GetCurrentState(TEntity entity);
    public abstract IEnumerable<TEventBase> GetUncommittedEvents(TEntity entity);

    // Required for reads
    public abstract TState CreateState();
    protected abstract TState Apply(TState state, TEventBase @event);
    protected abstract TEntity Create(TState state);

    object IEntityAdapter<TEntity, TEventBase>.GetCurrentState(TEntity entity) => GetCurrentState(entity);
    object IEntityAdapter<TEntity, TEventBase>.CreateState() => CreateState();

    async Task<TEntity> IEntityAdapter<TEntity, TEventBase>.RestoreAsync(
        object initialState,
        IAsyncEnumerable<TEventBase> events,
        CancellationToken cancellationToken)
    {
        var currentState = (TState)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            currentState = Apply(currentState, e);

        return Create(currentState);
    }
}

public abstract class EntityAdapter<TEntity, TEventBase> : EntityAdapter<TEntity, TEventBase, TEntity>
    where TEntity : notnull
    where TEventBase : class
{
    public override TEntity GetCurrentState(TEntity entity) => entity;
    protected override TEntity Create(TEntity state) => state;
}