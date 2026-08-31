using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public class FunctionalAggregateWrapper<TAggregate> where TAggregate : IIdentifiable, new()
{
    private readonly List<IDomainEvent> _raisedEvents = [];

    public Guid Id => Value.Id;
    public TAggregate Value { get; private set; } = new();
    public IEnumerable<IDomainEvent> RaisedEvents => _raisedEvents.AsReadOnly();

    public void Execute(Func<TAggregate, IEnumerable<IDomainEvent>> command)
    {
        var events = command(Value);

        // Apply to state
        foreach (var @event in events)
        {
            Apply(@event);
            _raisedEvents.Add(@event);
        }
    }

    public void Apply(object @event)
    {
        Value = ((dynamic)Value).Apply((dynamic)@event);
    }
}