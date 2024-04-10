using DomainBlocks.Persistence.Entities;
using DomainBlocks.Persistence.Tests.Integration.Model;

namespace DomainBlocks.Persistence.Tests.Integration.Adapters;

public sealed class MutableEntityAdapter<TEntity> : EntityAdapterBase<TEntity> where TEntity : MutableEntityBase, new()
{
    public override string GetId(TEntity entity) => entity.Id.ToString();
    public override IEnumerable<object> GetRaisedEvents(TEntity entity) => entity.RaisedEvents;
    public override TEntity CreateState() => new();

    protected override TEntity Fold(TEntity state, object @event)
    {
        state.Apply(@event);
        return state;
    }
}