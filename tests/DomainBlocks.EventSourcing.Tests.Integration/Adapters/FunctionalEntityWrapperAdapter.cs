using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class FunctionalEntityWrapperAdapter<TEntity> : EntityAdapterBase<FunctionalEntityWrapper<TEntity>>
    where TEntity : IIdentifiable, new()
{
    public override string GetId(FunctionalEntityWrapper<TEntity> entity) => entity.Id.ToString();

    public override IEnumerable<object> GetUncommittedEvents(FunctionalEntityWrapper<TEntity> entity) =>
        entity.RaisedEvents;

    public override FunctionalEntityWrapper<TEntity> CreateState() => new();

    protected override FunctionalEntityWrapper<TEntity> Apply(FunctionalEntityWrapper<TEntity> state, object @event)
    {
        state.Apply(@event);
        return state;
    }
}