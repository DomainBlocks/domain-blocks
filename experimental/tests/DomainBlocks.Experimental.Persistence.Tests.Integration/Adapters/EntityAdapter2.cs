using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Tests.Integration.Model;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration.Adapters;

public sealed class EntityAdapter2<TEntity, TState> : IEntityAdapter<TEntity>
    where TEntity : EntityBase<TState>, new()
    where TState : StateBase<TState>, new()
{
    public Type StateType => typeof(TState);
    public string GetId(TEntity entity) => entity.Id;
    public object GetCurrentState(TEntity entity) => entity.State;
    public IEnumerable<object> GetRaisedEvents(TEntity entity) => entity.RaisedEvents;
    public object CreateState() => new TState();

    public async Task<TEntity> RestoreEntity(
        object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken)
    {
        var state = (TState)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            state = state.Apply(e);
        }

        return new TEntity { State = state };
    }
}