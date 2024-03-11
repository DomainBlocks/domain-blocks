namespace DomainBlocks.IntegrationTests.Experimental.Model;

public class FunctionalEntityWrapper<TEntity> where TEntity : IIdentifiable, new()
{
    private readonly List<object> _raisedEvents = new();

    public Guid Id => Entity.Id;
    public TEntity Entity { get; private set; } = new();
    public IEnumerable<object> RaisedEvents => _raisedEvents.AsReadOnly();

    public void Execute(Func<TEntity, IEnumerable<object>> command)
    {
        var events = command(Entity);

        // Apply to state
        foreach (var @event in events)
        {
            Apply(@event);
            _raisedEvents.Add(@event);
        }
    }

    public void Apply(object @event)
    {
        Entity = ((dynamic)Entity).Apply((dynamic)@event);
    }
}