using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

// ReSharper disable UnusedParameter.Local

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class EntityAdapter<TEntity, TState> : EventSourcing.EntityAdapter<TEntity, TState>
    where TEntity : EntityBase<TState>, new()
    where TState : StateBase<TState>, new()
{
    // The unused parameters are used dynamically in tests
    public EntityAdapter(int i, string s)
    {
    }

    public override string GetId(TEntity entity) => entity.Id;
    public override TState GetCurrentState(TEntity entity) => entity.State;
    public override IEnumerable<object> GetUncommittedEvents(TEntity entity) => entity.UncommittedEvents;
    public override TState CreateState() => new();
    protected override TState Apply(TState state, object @event) => state.Apply(@event);
    protected override TEntity Create(TState state) => new() { State = state };
}