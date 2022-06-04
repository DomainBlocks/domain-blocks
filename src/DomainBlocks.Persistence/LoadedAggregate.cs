using System;
using System.Collections.Generic;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

internal static class LoadedAggregate
{
    internal static LoadedAggregate<TState, TEventBase> Create<TState, TEventBase>(
        TState state,
        Func<TState, TEventBase, TState> eventApplier,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoaded)
    {
        var aggregate = new Aggregate<TState, TEventBase>(state, eventApplier);
        return new LoadedAggregate<TState, TEventBase>(aggregate, id, version, snapshotVersion, eventsLoaded);
    }
}

public sealed class LoadedAggregate<TState, TEventBase>
{
    private readonly Aggregate<TState, TEventBase> _aggregate;

    internal LoadedAggregate(
        Aggregate<TState, TEventBase> aggregate,
        string id,
        long version,
        long? snapshotVersion,
        long loadedEventsCount)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));
        
        _aggregate = aggregate;

        Id = id;
        Version = version;
        SnapshotVersion = snapshotVersion;
        LoadedEventsCount = loadedEventsCount;
    }

    public TState State => _aggregate.State;
    public IReadOnlyList<TEventBase> AppliedEvents => _aggregate.AppliedEvents;
    public string Id { get; }
    public long Version { get; }
    public long? SnapshotVersion { get; }
    public long LoadedEventsCount { get; }
    internal bool IsSaved { get; set; }

    public void ExecuteCommand(Action<TState, Action<TState, TEventBase>> commandExecutor) =>
        _aggregate.ExecuteCommand(commandExecutor);

    public void ExecuteCommand(Func<TState, Func<TState, TEventBase, TState>, TState> commandExecutor) =>
        _aggregate.ExecuteCommand(commandExecutor);

    public void ExecuteCommand(Func<TState, IEnumerable<TEventBase>> commandExecutor) =>
        _aggregate.ExecuteCommand(commandExecutor);
}