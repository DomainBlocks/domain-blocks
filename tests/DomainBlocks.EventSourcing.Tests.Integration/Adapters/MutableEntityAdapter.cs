using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class MutableEntityAdapter<TEntity> : EntityAdapter<TEntity> where TEntity : MutableEntityBase, new()
{
    public override string GetId(TEntity entity) => entity.Id.ToString();
    public override IEnumerable<object> GetUncommittedEvents(TEntity entity) => entity.RaisedEvents;
    public override TEntity CreateState() => new();

    protected override TEntity Apply(TEntity state, object @event)
    {
        state.Apply(@event);
        return state;
    }
}