using DomainBlocks.Persistence.Entities;
using Shopping.Domain.New;

namespace Shopping.Api2;

public sealed class AggregateAdapter<TEntity> : EntityAdapterBase<TEntity> where TEntity : AggregateBase, new()
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