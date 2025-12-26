using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class FunctionalAggregateWrapperAdapter<TEntity> :
    EntityAdapter<FunctionalAggregateWrapper<TEntity>, IDomainEvent>
    where TEntity : IIdentifiable, new()
{
    public override string GetId(FunctionalAggregateWrapper<TEntity> aggregate) => aggregate.Id.ToString();

    public override IEnumerable<IDomainEvent> GetUncommittedEvents(FunctionalAggregateWrapper<TEntity> aggregate) =>
        aggregate.RaisedEvents;

    public override FunctionalAggregateWrapper<TEntity> CreateState() => new();

    protected override FunctionalAggregateWrapper<TEntity> Apply(
        FunctionalAggregateWrapper<TEntity> state,
        IDomainEvent @event)
    {
        state.Apply(@event);
        return state;
    }
}