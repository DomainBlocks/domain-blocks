using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;
using DomainBlocks.IntegrationTests.Experimental.Model;

namespace DomainBlocks.IntegrationTests.Experimental.Persistence;

public class MutableEntityAdapter<TEntity> : IEntityAdapter<TEntity> where TEntity : MutableEntityBase, new()
{
    public string GetId(TEntity entity) => entity.Id.ToString();
    public IEnumerable<object> GetRaisedEvents(TEntity entity) => entity.RaisedEvents;
    public TEntity CreateState() => new();

    public TEntity Fold(TEntity state, object @event)
    {
        state.Apply(@event);
        return state;
    }
}