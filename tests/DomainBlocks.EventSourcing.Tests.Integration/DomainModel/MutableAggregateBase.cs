using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public abstract class MutableAggregateBase
{
    private readonly List<IDomainEvent> _raisedEvents = [];

    public abstract Guid Id { get; }
    public IEnumerable<IDomainEvent> RaisedEvents => _raisedEvents.AsReadOnly();

    public void Apply(object @event)
    {
        ((dynamic)this).Apply((dynamic)@event);
    }

    protected void Raise<TEvent>(TEvent @event) where TEvent : IDomainEvent
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));
        Apply(@event);
        _raisedEvents.Add(@event);
    }
}