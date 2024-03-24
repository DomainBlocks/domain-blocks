using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Tests.Integration.Model;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration.Adapters;

public sealed class FunctionalEntityWrapperAdapter<TEntity> : EntityAdapterBase<FunctionalEntityWrapper<TEntity>>
    where TEntity : IIdentifiable, new()
{
    public override string GetId(FunctionalEntityWrapper<TEntity> entity) => entity.Id.ToString();
    public override IEnumerable<object> GetRaisedEvents(FunctionalEntityWrapper<TEntity> entity) => entity.RaisedEvents;
    public override FunctionalEntityWrapper<TEntity> CreateState() => new();

    protected override FunctionalEntityWrapper<TEntity> Fold(FunctionalEntityWrapper<TEntity> state, object @event)
    {
        state.Apply(@event);
        return state;
    }
}