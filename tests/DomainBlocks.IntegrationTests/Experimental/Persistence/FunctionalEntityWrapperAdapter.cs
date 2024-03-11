using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;
using DomainBlocks.IntegrationTests.Experimental.Model;

namespace DomainBlocks.IntegrationTests.Experimental.Persistence;

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