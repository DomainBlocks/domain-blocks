using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Persistence.New;

namespace DomainBlocks.Persistence;

internal static class LoadedAggregate
{
    internal static LoadedAggregate<TAggregateState> Create<TAggregateState>(
        TAggregateState aggregateState,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoaded,
        IAggregateType<TAggregateState> aggregateType)
    {
        return new LoadedAggregate<TAggregateState>(
            aggregateState, id, version, snapshotVersion, eventsLoaded, aggregateType);
    }
}

public sealed class LoadedAggregate<TAggregateState>
{
    private readonly IAggregateType<TAggregateState> _aggregateType;

    internal LoadedAggregate(
        TAggregateState aggregateState,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoadedCount,
        IAggregateType<TAggregateState> aggregateType)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        _aggregateType = aggregateType ?? throw new ArgumentNullException(nameof(aggregateType));

        AggregateState = aggregateState ?? throw new ArgumentNullException(nameof(aggregateState));
        Id = id;
        Version = version;
        SnapshotVersion = snapshotVersion;
        EventsLoadedCount = eventsLoadedCount;
        EventsToPersist = Enumerable.Empty<object>();
    }

    public TAggregateState AggregateState { get; private set; }
    public string Id { get; }
    public long Version { get; }
    public long? SnapshotVersion { get; }
    public long EventsLoadedCount { get; }
    public IEnumerable<object> EventsToPersist { get; private set; }
    internal bool HasBeenSaved { get; set; }

    public TResult ExecuteCommand<TResult>(Func<TAggregateState, TResult> commandExecutor)
    {
        var commandResultType = _aggregateType.GetCommandResultType<TResult>();
        var result = commandExecutor(AggregateState);
        var (updatedState, events) = commandResultType.GetUpdatedStateAndEvents(result, AggregateState);
        AggregateState = updatedState;
        EventsToPersist = EventsToPersist.Concat(events);

        return result;
    }

    public void ExecuteCommand(Action<TAggregateState> commandExecutor)
    {
        var commandResultType = _aggregateType.GetVoidCommandResultType();
        commandExecutor(AggregateState);
        var (updatedState, events) = commandResultType.GetUpdatedStateAndEvents(AggregateState);
        AggregateState = updatedState;
        EventsToPersist = EventsToPersist.Concat(events);
    }
}