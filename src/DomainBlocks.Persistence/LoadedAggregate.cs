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
        _eventDispatcher = eventDispatcher;

        AggregateState = aggregateState ?? throw new ArgumentNullException(nameof(aggregateState));
        Id = id;
        Version = version;
        SnapshotVersion = snapshotVersion;
        EventsLoadedCount = eventsLoadedCount;
        EventsToPersist = Enumerable.Empty<TEventBase>();
    }

    // public void DispatchCommand<TCommand>(TCommand command) where TCommand : TCommandBase
    // {
    //     var events = _commandDispatcher.Dispatch(AggregateState, command);
    //     EventsToPersist = EventsToPersist.Concat(events);
    // }
    //
    // public void ImmutableDispatchCommand<TCommand>(TCommand command) where TCommand : TCommandBase
    // {
    //     var (newState, events) = _commandDispatcher.ImmutableDispatch(AggregateState, command);
    //     EventsToPersist = EventsToPersist.Concat(events);
    //     AggregateState = newState;
    // }

    // public void ExecuteCommand(Action<TAggregateState> commandExecutor)
    // {
    //     
    // }
    //
    // public void ExecuteCommand(Action<TAggregateState, IEventDispatcher<TEventBase>> commandExecutor)
    // {
    //     
    // }
    
    public void ExecuteCommand(Func<TAggregateState, IEnumerable<TEventBase>> commandExecutor)
    {
        var events = commandExecutor(AggregateState).ToList();
        AggregateState = _eventDispatcher.Dispatch(AggregateState, events);
        EventsToPersist = EventsToPersist.Concat(events);
        _eventDispatcher.ClearTrackedEvents();
    }

    // public void ExecuteCommand(Func<TAggregateState, IEventDispatcher<TEventBase>, TAggregateState> commandExecutor)
    // {
    //     
    // }

    public string Id { get; }
    public TAggregateState AggregateState { get; private set; }
    public long Version { get; }
    public long? SnapshotVersion { get; }
    public long EventsLoadedCount { get; }
    public IEnumerable<TEventBase> EventsToPersist { get; private set; }
    internal bool HasBeenSaved { get; set; }
}