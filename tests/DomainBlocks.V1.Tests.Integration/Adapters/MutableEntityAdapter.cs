using DomainBlocks.V1.Persistence.Entities;
using DomainBlocks.V1.Tests.Integration.Model;

namespace DomainBlocks.V1.Tests.Integration.Adapters;

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