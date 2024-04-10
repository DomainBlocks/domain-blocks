using DomainBlocks.Persistence.Entities;
using DomainBlocks.Persistence.Tests.Integration.Model;

namespace DomainBlocks.Persistence.Tests.Integration.Adapters;

public sealed class EntityAdapter<TEntity, TState> : EntityAdapterBase<TEntity, TState>
    where TEntity : EntityBase<TState>, new()
    where TState : StateBase<TState>, new()
{
    public EntityAdapter(int i, string s)
    {
    }

    public override string GetId(TEntity entity) => entity.Id;
    public override TState GetCurrentState(TEntity entity) => entity.State;
    public override IEnumerable<object> GetRaisedEvents(TEntity entity) => entity.RaisedEvents;
    public override TState CreateState() => new();
    protected override TState Fold(TState state, object @event) => state.Apply(@event);
    protected override TEntity Create(TState state) => new() { State = state };
}