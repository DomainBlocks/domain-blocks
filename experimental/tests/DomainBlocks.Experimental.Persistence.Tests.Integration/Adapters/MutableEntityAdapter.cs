using DomainBlocks.Experimental.Persistence.Adapters;
using DomainBlocks.Experimental.Persistence.Tests.Integration.Model;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration.Adapters;

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