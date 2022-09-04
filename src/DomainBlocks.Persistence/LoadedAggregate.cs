﻿using System;
using System.Collections.Generic;
using DomainBlocks.Core;

namespace DomainBlocks.Persistence;

internal static class LoadedAggregate
{
    internal static LoadedAggregate<TAggregate> Create<TAggregate>(
        TAggregate state,
        IAggregateType<TAggregate> aggregateType,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoaded)
    {
        return new LoadedAggregate<TAggregate>(state, aggregateType, id, version, snapshotVersion, eventsLoaded);
    }
}

public sealed class LoadedAggregate<TAggregate>
{
    private readonly ICommandExecutionContext<TAggregate> _commandExecutionContext;

    internal LoadedAggregate(
        TAggregate state,
        IAggregateType<TAggregate> aggregateType,
        string id,
        long version,
        long? snapshotVersion,
        long eventsLoadedCount)
    {
        if (aggregateType == null) throw new ArgumentNullException(nameof(aggregateType));

        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        _commandExecutionContext = aggregateType.GetCommandExecutionContext(state);

        Id = id;
        Version = version;
        SnapshotVersion = snapshotVersion;
        EventsLoadedCount = eventsLoadedCount;
    }

    public TAggregate State => _commandExecutionContext.State;
    public string Id { get; }
    public long Version { get; }
    public long? SnapshotVersion { get; }
    public long EventsLoadedCount { get; }
    public IEnumerable<object> EventsToPersist => _commandExecutionContext.RaisedEvents;
    internal bool HasBeenSaved { get; set; }

    public TResult ExecuteCommand<TResult>(Func<TAggregate, TResult> commandExecutor)
    {
        return _commandExecutionContext.ExecuteCommand(commandExecutor);
    }

    public void ExecuteCommand(Action<TAggregate> commandExecutor)
    {
        _commandExecutionContext.ExecuteCommand(commandExecutor);
    }
}