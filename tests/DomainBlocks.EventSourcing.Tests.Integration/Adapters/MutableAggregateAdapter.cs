using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class MutableAggregateAdapter<TAggregate> :
    EntityAdapter<TAggregate, IDomainEvent>
    where TAggregate : MutableAggregateBase, new()
{
    public override string GetId(TAggregate entity) => entity.Id.ToString();
    public override IEnumerable<IDomainEvent> GetUncommittedEvents(TAggregate entity) => entity.RaisedEvents;
    public override TAggregate CreateState() => new();

    protected override TAggregate Apply(TAggregate state, IDomainEvent @event)
    {
        state.Apply(@event);
        return state;
    }
}