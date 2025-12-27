using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.EntityDefinitions;

public sealed class FunctionalAggregateWrapperDefinition<TEntity> :
    EntityDefinition<IDomainEvent, FunctionalAggregateWrapper<TEntity>>
    where TEntity : IIdentifiable, new()
{
    public override string GetId(FunctionalAggregateWrapper<TEntity> aggregate) => aggregate.Id.ToString();

    public override IEnumerable<IDomainEvent> GetUncommittedEvents(FunctionalAggregateWrapper<TEntity> aggregate) =>
        aggregate.RaisedEvents;

    public override FunctionalAggregateWrapper<TEntity> CreateInitialState() => new();

    protected override FunctionalAggregateWrapper<TEntity> Apply(
        FunctionalAggregateWrapper<TEntity> state,
        IDomainEvent @event)
    {
        state.Apply(@event);
        return state;
    }
}