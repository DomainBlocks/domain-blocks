using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

// ReSharper disable UnusedParameter.Local

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class AggregateAdapter<TAggregate, TState> : EntityAdapter<TAggregate, TState>
    where TAggregate : Aggregate<TState>, new()
    where TState : StateBase<TState>, new()
{
#pragma warning disable IDE0060 // The unused parameters are used dynamically in tests
    public AggregateAdapter(int i, string s)
#pragma warning restore IDE0060
    {
    }

    public override string GetId(TAggregate entity) => entity.Id;
    public override TState GetCurrentState(TAggregate entity) => entity.State;
    public override IEnumerable<object> GetUncommittedEvents(TAggregate entity) => entity.UncommittedEvents;
    public override TState CreateState() => new();
    protected override TState Apply(TState state, object @event) => state.Apply(@event);
    protected override TAggregate Create(TState state) => new() { State = state };
}