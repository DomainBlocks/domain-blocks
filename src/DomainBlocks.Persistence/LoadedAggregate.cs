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
        TrackingEventDispatcher<TEventBase> trackingEventDispatcher)
    {
        return new LoadedAggregate<TAggregateState, TEventBase>(
            aggregateState, id, version, snapshotVersion, eventsLoaded, trackingEventDispatcher);
    }
}

public sealed class LoadedAggregate<TAggregateState, TEventBase>
{
    private readonly TrackingEventDispatcher<TEventBase> _eventDispatcher;

    internal LoadedAggregate(
        TAggregateState aggregateState,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoadedCount,
        TrackingEventDispatcher<TEventBase> eventDispatcher)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        
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
        // Mutates the aggregate state.
        commandExecutor(AggregateState);

        // Save any events that were raised on the event dispatcher.
        EventsToPersist = EventsToPersist.Concat(_eventDispatcher.TrackedEvents);
        _eventDispatcher.ClearTrackedEvents();
    }
    
    public void ExecuteCommand(Action<TAggregateState, IEventDispatcher<TEventBase>> commandExecutor)
    {
        // Mutates the aggregate state.
        commandExecutor(AggregateState, _eventDispatcher);

        // Save any events that were raised on the event dispatcher.
        EventsToPersist = EventsToPersist.Concat(_eventDispatcher.TrackedEvents);
        _eventDispatcher.ClearTrackedEvents();
    }
    
    public void ExecuteCommand(Func<TAggregateState, TAggregateState> commandExecutor)
    {
        // Get the new state by executing the command.
        AggregateState = commandExecutor(AggregateState);

        // Save any events that were raised on the event dispatcher.
        EventsToPersist = EventsToPersist.Concat(_eventDispatcher.TrackedEvents);
        _eventDispatcher.ClearTrackedEvents();
    }

    public void ExecuteCommand(Func<TAggregateState, IEventDispatcher<TEventBase>, TAggregateState> commandExecutor)
    {
        // Get the new state by executing the command.
        AggregateState = commandExecutor(AggregateState, _eventDispatcher);

        // Save any events that were raised on the event dispatcher.
        EventsToPersist = EventsToPersist.Concat(_eventDispatcher.TrackedEvents);
        _eventDispatcher.ClearTrackedEvents();
    }
    
    public void ExecuteCommand(Func<TAggregateState, IEnumerable<TEventBase>> commandExecutor)
    {
        // Get the events to raise by executing the command. This func is assumed to be immutable.
        var events = commandExecutor(AggregateState).ToList();

        // Dispatch the events, and get the new state.
        AggregateState = _eventDispatcher.Dispatch(AggregateState, events);

        // Save the events. Note that any events raised on the event dispatcher by the command executor are ignored.
        EventsToPersist = EventsToPersist.Concat(events);
        _eventDispatcher.ClearTrackedEvents();
    }
}