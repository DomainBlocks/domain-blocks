using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class EntityAdapter2<TEntity, TState> : IEntityAdapter<TEntity>
    where TEntity : EntityBase<TState>, new()
    where TState : StateBase<TState>, new()
{
    public Type StateType => typeof(TState);
    public string GetId(TEntity entity) => entity.Id;
    public object GetCurrentState(TEntity entity) => entity.State;
    public IEnumerable<object> GetUncommittedEvents(TEntity entity) => entity.UncommittedEvents;
    public object CreateState() => new TState();

    public async Task<TEntity> RestoreAsync(
        object initialState,
        IAsyncEnumerable<object> events,
        CancellationToken cancellationToken)
    {
        var state = (TState)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            state = state.Apply(e);
        }

        return new TEntity { State = state };
    }
}