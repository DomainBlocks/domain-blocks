using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Tests.Integration.Model;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration.Adapters;

public class FunctionalEntityWrapperAdapter<TEntity> : IEntityAdapter<FunctionalEntityWrapper<TEntity>>
    where TEntity : IIdentifiable, new()
{
    public string GetId(FunctionalEntityWrapper<TEntity> entity) => entity.Id.ToString();
    public IEnumerable<object> GetRaisedEvents(FunctionalEntityWrapper<TEntity> entity) => entity.RaisedEvents;
    public FunctionalEntityWrapper<TEntity> CreateState() => new();

    public FunctionalEntityWrapper<TEntity> Fold(FunctionalEntityWrapper<TEntity> state, object @event)
    {
        state.Apply(@event);
        return state;
    }
}