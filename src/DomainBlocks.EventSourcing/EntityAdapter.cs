namespace DomainBlocks.EventSourcing;

public abstract class EntityAdapter<TEventBase, TEntity, TState> : IEntityAdapter<TEventBase, TEntity>
    where TEventBase : class
    where TEntity : notnull
    where TState : notnull
{
    public Type StateType => typeof(TState);

    // Required for writes
    public abstract string GetId(TEntity entity);
    public abstract TState GetState(TEntity entity);
    public abstract IEnumerable<TEventBase> GetUncommittedEvents(TEntity entity);

    // Required for reads
    public abstract TState CreateInitialState();
    protected abstract TState Apply(TState state, TEventBase @event);
    protected abstract TEntity CreateFromState(TState state);

    object IEntityAdapter<TEventBase, TEntity>.GetState(TEntity entity) => GetState(entity);
    object IEntityAdapter<TEventBase, TEntity>.CreateInitialState() => CreateInitialState();

    async Task<TEntity> IEntityAdapter<TEventBase, TEntity>.RestoreAsync(
        object initialState,
        IAsyncEnumerable<TEventBase> events,
        CancellationToken cancellationToken)
    {
        var currentState = (TState)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            currentState = Apply(currentState, e);

        return CreateFromState(currentState);
    }
}

public abstract class EntityAdapter<TEventBase, TEntity> : EntityAdapter<TEventBase, TEntity, TEntity>
    where TEventBase : class
    where TEntity : notnull
{
    public override TEntity GetState(TEntity entity) => entity;
    protected override TEntity CreateFromState(TEntity state) => state;
}