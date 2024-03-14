namespace DomainBlocks.Experimental.Persistence.Adapters;

public class EntityAdapter<TEntity> : IEntityAdapter
{
    public EntityAdapter(
        Type stateType,
        Func<TEntity, string> idSelector,
        Func<TEntity, object> currentStateSelector,
        Func<TEntity, IEnumerable<object>> raisedEventsSelector,
        EntityRestorer<TEntity> entityRestorer,
        Func<object> stateFactory)
    {
        StateType = stateType;
        IdSelector = idSelector;
        CurrentStateSelector = currentStateSelector;
        RaisedEventsSelector = raisedEventsSelector;
        EntityRestorer = entityRestorer;
        StateFactory = stateFactory;
    }

    public Type EntityType => typeof(TEntity);
    public Type StateType { get; }
    public Func<TEntity, string> IdSelector { get; }
    public Func<TEntity, object> CurrentStateSelector { get; }
    public Func<TEntity, IEnumerable<object>> RaisedEventsSelector { get; }
    public EntityRestorer<TEntity> EntityRestorer { get; }
    public Func<object> StateFactory { get; }
}