using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;
using DomainBlocks.IntegrationTests.Experimental.Model;

namespace DomainBlocks.IntegrationTests.Experimental.Persistence;

public class EntityAdapter<TEntity, TState> : IEntityAdapter<TEntity, TState>
    where TEntity : EntityBase<TState>, new()
    where TState : StateBase<TState>, new()
{
    public string GetId(TEntity entity) => entity.Id;
    public TState GetCurrentState(TEntity entity) => entity.State;
    public IEnumerable<object> GetRaisedEvents(TEntity entity) => entity.RaisedEvents;
    public TState CreateState() => new();
    public TState Fold(TState state, object @event) => state.Apply(@event);
    public TEntity Create(TState state) => new() { State = state };
}