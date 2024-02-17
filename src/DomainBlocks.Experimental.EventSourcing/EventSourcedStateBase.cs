using System.Collections.Immutable;

namespace DomainBlocks.Experimental.EventSourcing;

public abstract class EventSourcedStateBase<TState> : IEventTypeMapSource<TState>
    where TState : EventSourcedStateBase<TState>, new()
{
    private static readonly EventTypeMap<TState> EventTypes;

    private ImmutableList<object> _raisedEvents = ImmutableList<object>.Empty;

    static EventSourcedStateBase()
    {
        var builder = EventTypeMap.CreateBuilder<TState>();
        new TState().OnConfiguring(builder);
        EventTypes = builder.Build();
    }

    EventTypeMap<TState> IEventTypeMapSource<TState>.EventTypeMap => EventTypes;

    public IReadOnlyList<object> RaisedEvents => _raisedEvents;

    protected TState RaiseEvent<TEvent>(TEvent @event)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        var mapping = EventTypes[typeof(TEvent)];
        var state = mapping.EventApplier!.Invoke((TState)this, @event); // TODO
        state._raisedEvents = _raisedEvents.Add(@event);

        return state;
    }

    protected virtual void OnConfiguring(EventTypeMap<TState>.Builder eventTypes)
    {
    }
}