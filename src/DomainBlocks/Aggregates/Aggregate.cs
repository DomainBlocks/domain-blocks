using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Aggregates;

public static class Aggregate
{
    public static Aggregate<TState, TEventBase> Create<TState, TEventBase>(
        EventRoutes<TEventBase> eventRoutes,
        Func<IAggregateEventRouter<TEventBase>, TState> stateFactory,
        IEnumerable<TEventBase> events = null)
    {
        if (eventRoutes == null) throw new ArgumentNullException(nameof(eventRoutes));
        if (stateFactory == null) throw new ArgumentNullException(nameof(stateFactory));

        var eventRouter = new AggregateEventRouter<TEventBase>(eventRoutes);
        var trackingEventRouter = new TrackingAggregateEventRouter<TEventBase>(eventRouter);
        
        // Create the initial state.
        var state = stateFactory(trackingEventRouter);
        
        // Apply any given events to the initial state. Note that we don't care about tracking the events here.
        state = eventRouter.Send(state, events ?? Enumerable.Empty<TEventBase>());
        
        return new Aggregate<TState, TEventBase>(state, trackingEventRouter);
    }
    
    // TODO (DS): Find a way to get rid of this overload, as we want to enforce (as much as we can) that a given
    // TState instance and it's wrapping Aggregate instance share the same TrackingAggregateEventRouter instance.
    // This comes back to the topic of being able to instantiate state from a snapshot with an event router.
    public static Aggregate<TState, TEventBase> Create<TState, TEventBase>(
        TState state, TrackingAggregateEventRouter<TEventBase> eventRouter)
    {
        return new Aggregate<TState, TEventBase>(state, eventRouter);
    }
}

public class Aggregate<TState, TEventBase>
{
    private readonly TrackingAggregateEventRouter<TEventBase> _eventRouter;
    private readonly List<TEventBase> _raisedEvents = new();

    internal Aggregate(TState state, TrackingAggregateEventRouter<TEventBase> eventRouter)
    {
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public TState State { get; private set; }
    public IReadOnlyList<TEventBase> RaisedEvents => _raisedEvents.AsReadOnly();

    public void ClearRaisedEvents() => _raisedEvents.Clear();
    
    public void ExecuteCommand(Action<TState> commandExecutor)
    {
        ExecuteCommand(agg =>
        {
            commandExecutor(agg);
            return agg;
        });
    }
    
    public void ExecuteCommand(Action<TState, IAggregateEventRouter<TEventBase>> commandExecutor)
    {
        ExecuteCommand(agg =>
        {
            commandExecutor(agg, _eventRouter);
            return agg;
        });
    }
    
    public void ExecuteCommand(Func<TState, TState> commandExecutor)
    {
        // Get the new state by executing the command.
        State = commandExecutor(State);

        // Save any events that were raised on the event router.
        _raisedEvents.AddRange(_eventRouter.TrackedEvents);
        _eventRouter.ClearTrackedEvents();
    }

    public void ExecuteCommand(Func<TState, IAggregateEventRouter<TEventBase>, TState> commandExecutor)
    {
        ExecuteCommand(agg => commandExecutor(agg, _eventRouter));
    }
    
    public void ExecuteCommand(Func<TState, IEnumerable<TEventBase>> commandExecutor)
    {
        // Get the events to raise by executing the command. This func is assumed to be immutable.
        var events = commandExecutor(State).ToList();

        // Send the events, and get the new state.
        State = _eventRouter.Send(State, events);

        // Save the events. Note that any events raised on the event router by the command executor are ignored.
        _raisedEvents.AddRange(events);
        _eventRouter.ClearTrackedEvents();
    }
}