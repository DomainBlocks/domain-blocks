using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

internal static class LoadedAggregate
{
    internal static LoadedAggregate<TAggregateState, TEventBase> Create<TAggregateState, TEventBase>(
        TAggregateState aggregateState,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoaded,
        TrackingAggregateEventRouter<TEventBase> trackingEventRouter)
    {
        return new LoadedAggregate<TAggregateState, TEventBase>(
            aggregateState, id, version, snapshotVersion, eventsLoaded, trackingEventRouter);
    }
}

public sealed class LoadedAggregate<TAggregateState, TEventBase>
{
    private readonly TrackingAggregateEventRouter<TEventBase> _eventRouter;

    internal LoadedAggregate(
        TAggregateState aggregateState,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoadedCount,
        TrackingAggregateEventRouter<TEventBase> eventRouter)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
        
        AggregateState = aggregateState ?? throw new ArgumentNullException(nameof(aggregateState));
        Id = id;
        Version = version;
        SnapshotVersion = snapshotVersion;
        EventsLoadedCount = eventsLoadedCount;
        EventsToPersist = Enumerable.Empty<TEventBase>();
    }

    public TAggregateState AggregateState { get; private set; }
    public string Id { get; }
    public long Version { get; }
    public long? SnapshotVersion { get; }
    public long EventsLoadedCount { get; }
    public IEnumerable<TEventBase> EventsToPersist { get; private set; }
    internal bool HasBeenSaved { get; set; }
    
    public void ExecuteCommand(Action<TAggregateState> commandExecutor)
    {
        ExecuteCommand(agg =>
        {
            commandExecutor(agg);
            return agg;
        });
    }
    
    public void ExecuteCommand(Action<TAggregateState, IAggregateEventRouter<TEventBase>> commandExecutor)
    {
        ExecuteCommand(agg =>
        {
            commandExecutor(agg, _eventRouter);
            return agg;
        });
    }
    
    public void ExecuteCommand(Func<TAggregateState, TAggregateState> commandExecutor)
    {
        // Get the new state by executing the command.
        AggregateState = commandExecutor(AggregateState);

        // Save any events that were raised on the event router.
        EventsToPersist = EventsToPersist.Concat(_eventRouter.TrackedEvents);
        _eventRouter.ClearTrackedEvents();
    }

    public void ExecuteCommand(Func<TAggregateState, IAggregateEventRouter<TEventBase>, TAggregateState> commandExecutor)
    {
        ExecuteCommand(agg => commandExecutor(agg, _eventRouter));
    }
    
    public void ExecuteCommand(Func<TAggregateState, IEnumerable<TEventBase>> commandExecutor)
    {
        // Get the events to raise by executing the command. This func is assumed to be immutable.
        var events = commandExecutor(AggregateState).ToList();

        // Send the events, and get the new state.
        AggregateState = _eventRouter.Send(AggregateState, events);

        // Save the events. Note that any events raised on the event router by the command executor are ignored.
        EventsToPersist = EventsToPersist.Concat(events);
        _eventRouter.ClearTrackedEvents();
    }
}