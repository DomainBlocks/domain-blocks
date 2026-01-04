namespace DomainBlocks.EventSourcing;

public abstract class EntityDefinition<TEvent, TEntity, TState> : IEntityDefinition<TEvent, TEntity>
    where TEvent : notnull
    where TEntity : notnull
    where TState : notnull
{
    public Type StateType => typeof(TState);

    // Required for writes
    public abstract string GetId(TEntity entity);
    public abstract TState GetState(TEntity entity);
    public abstract IEnumerable<TEvent> GetUncommittedEvents(TEntity entity);

    // Required for reads
    public abstract TState CreateInitialState();
    protected abstract TState Apply(TState state, TEvent @event);
    protected abstract TEntity CreateFromState(TState state);

    object IEntityDefinition<TEvent, TEntity>.GetState(TEntity entity) => GetState(entity);
    object IEntityDefinition<TEvent, TEntity>.CreateInitialState() => CreateInitialState();

    async Task<TEntity> IEntityDefinition<TEvent, TEntity>.RestoreAsync(
        object initialState,
        IAsyncEnumerable<TEvent> events,
        CancellationToken cancellationToken)
    {
        var currentState = (TState)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            currentState = Apply(currentState, e);

        return CreateFromState(currentState);
    }
}

public abstract class EntityDefinition<TEvent, TEntity> : EntityDefinition<TEvent, TEntity, TEntity>
    where TEvent : notnull
    where TEntity : notnull
{
    public override TEntity GetState(TEntity entity) => entity;
    protected override TEntity CreateFromState(TEntity state) => state;
}