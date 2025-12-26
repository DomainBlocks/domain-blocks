using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

// ReSharper disable UnusedParameter.Local
namespace DomainBlocks.EventSourcing.Tests.Integration.EntityDefinitions;

public sealed class AggregateDefinition<TAggregate, TState> : EntityDefinition<IDomainEvent, TAggregate, TState>
    where TAggregate : Aggregate<TState>, new()
    where TState : StateBase<TState>, new()
{
#pragma warning disable IDE0060 // The unused parameters are used dynamically in tests
    public AggregateDefinition(int i, string s)
#pragma warning restore IDE0060
    {
    }

    public override string GetId(TAggregate entity) => entity.Id;
    public override TState GetState(TAggregate entity) => entity.State;
    public override IEnumerable<IDomainEvent> GetUncommittedEvents(TAggregate entity) => entity.UncommittedEvents;
    public override TState CreateInitialState() => new();
    protected override TState Apply(TState state, IDomainEvent @event) => state.Apply(@event);
    protected override TAggregate CreateFromState(TState state) => new() { State = state };
}