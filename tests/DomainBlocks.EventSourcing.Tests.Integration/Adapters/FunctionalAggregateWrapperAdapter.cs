using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class FunctionalAggregateWrapperAdapter<TEntity> : EntityAdapter<FunctionalAggregateWrapper<TEntity>>
    where TEntity : IIdentifiable, new()
{
    public override string GetId(FunctionalAggregateWrapper<TEntity> aggregate) => aggregate.Id.ToString();

    public override IEnumerable<object> GetUncommittedEvents(FunctionalAggregateWrapper<TEntity> aggregate) =>
        aggregate.RaisedEvents;

    public override FunctionalAggregateWrapper<TEntity> CreateState() => new();

    protected override FunctionalAggregateWrapper<TEntity> Apply(
        FunctionalAggregateWrapper<TEntity> state,
        object @event)
    {
        state.Apply(@event);
        return state;
    }
}