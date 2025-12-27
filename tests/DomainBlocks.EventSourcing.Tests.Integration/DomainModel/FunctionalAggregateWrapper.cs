using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public class FunctionalAggregateWrapper<TEntity> where TEntity : IIdentifiable, new()
{
    private readonly List<IDomainEvent> _raisedEvents = [];

    public Guid Id => Entity.Id;
    public TEntity Entity { get; private set; } = new();
    public IEnumerable<IDomainEvent> RaisedEvents => _raisedEvents.AsReadOnly();

    public void Execute(Func<TEntity, IEnumerable<IDomainEvent>> command)
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