using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Aggregates;

public static class Aggregate
{
    public static Aggregate<TState, TEventBase> Create<TState, TEventBase>(
        TState state,
        Action<TState, TEventBase> eventApplier,
        IEnumerable<TEventBase> events = null)
    {
        return Create(
            state,
            (s, e) =>
            {
                eventApplier(s, e);
                return s;
            },
            events);
    }

    public static Aggregate<TState, TEventBase> Create<TState, TEventBase>(
        TState state,
        Func<TState, TEventBase, TState> eventApplier,
        IEnumerable<TEventBase> events = null)
    {
        if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));

        // Apply any given events to the initial state.
        if (events is not null)
        {
            state = events.Aggregate(state, eventApplier);
        }

        return new Aggregate<TState, TEventBase>(state, eventApplier);
    }
}

public class Aggregate<TState, TEventBase>
{
    private readonly Func<TState, TEventBase, TState> _eventApplier;
    private readonly List<TEventBase> _appliedEvents = new();

    public Aggregate(TState state, Func<TState, TEventBase, TState> eventApplier)
    {
        _eventApplier = eventApplier ?? throw new ArgumentNullException(nameof(eventApplier));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public TState State { get; private set; }
    public IReadOnlyList<TEventBase> AppliedEvents => _appliedEvents.AsReadOnly();

    public void ClearAppliedEvents() => _appliedEvents.Clear();

    public void ExecuteCommand(Action<TState, Action<TState, TEventBase>> commandExecutor)
    {
        ExecuteCommand((state, eventApplier) =>
        {
            commandExecutor(state, (s, e) => eventApplier(s, e));
            return state;
        });
    }

    public void ExecuteCommand(Func<TState, Func<TState, TEventBase, TState>, TState> commandExecutor)
    {
        State = commandExecutor(State, EventApplier);
    }

    public void ExecuteCommand(Func<TState, IEnumerable<TEventBase>> commandExecutor)
    {
        var events = commandExecutor(State);
        State = events.Aggregate(State, EventApplier);
    }

    private TState EventApplier(TState state, TEventBase @event)
    {
        _appliedEvents.Add(@event);
        return _eventApplier(state, @event);
    }
}