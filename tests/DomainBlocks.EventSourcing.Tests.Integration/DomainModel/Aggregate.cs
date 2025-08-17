using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public abstract class Aggregate<TState> where TState : StateBase<TState>, new()
{
    private readonly List<IDomainEvent> _uncommittedEvents = [];
    private TState _state = new();

    public abstract string Id { get; }

    public TState State
    {
        get => _state;
        init => _state = value;
    }

    public IEnumerable<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    protected void Raise<TEvent>(TEvent @event) where TEvent : IDomainEvent
    {
        _state = _state.Apply(@event);
        _uncommittedEvents.Add(@event);
    }
}