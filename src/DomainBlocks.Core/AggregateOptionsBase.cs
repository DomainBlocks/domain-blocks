using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public abstract class AggregateOptionsBase<TAggregate, TEventBase> : IAggregateOptions<TAggregate>
{
    private readonly Dictionary<Type, ICommandResultOptions> _commandResultsOptions = new();
    private readonly Dictionary<Type, IEventOptions> _eventsOptions = new();

    protected AggregateOptionsBase()
    {
    }

    protected AggregateOptionsBase(AggregateOptionsBase<TAggregate, TEventBase> copyFrom)
    {
        if (copyFrom == null) throw new ArgumentNullException(nameof(copyFrom));

        Factory = copyFrom.Factory;
        IdSelector = copyFrom.IdSelector;
        IdToStreamKeySelector = copyFrom.IdToStreamKeySelector;
        IdToSnapshotKeySelector = copyFrom.IdToSnapshotKeySelector;
        EventApplier = copyFrom.EventApplier;
        _commandResultsOptions = new Dictionary<Type, ICommandResultOptions>(copyFrom._commandResultsOptions);
        _eventsOptions = new Dictionary<Type, IEventOptions>(copyFrom._eventsOptions);
    }

    public Type ClrType => typeof(TAggregate);
    public Type EventBaseType => typeof(TEventBase);
    public IEnumerable<IEventOptions> EventsOptions => _eventsOptions.Values;

    public Func<TAggregate> Factory { get; private set; }
    public Func<TAggregate, string> IdSelector { get; private set; }
    public Func<string, string> IdToStreamKeySelector { get; private set; }
    public Func<string, string> IdToSnapshotKeySelector { get; private set; }
    public Func<TAggregate, object, TAggregate> EventApplier { get; private set; }

    public string SelectStreamKey(TAggregate aggregate) => IdToStreamKeySelector(IdSelector(aggregate));
    public string SelectSnapshotKey(TAggregate aggregate) => IdToSnapshotKeySelector(IdSelector(aggregate));

    public abstract ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);

    public AggregateOptionsBase<TAggregate, TEventBase> WithFactory(Func<TAggregate> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var clone = Clone();
        clone.Factory = factory;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithIdSelector(Func<TAggregate, string> idSelector)
    {
        if (idSelector == null) throw new ArgumentNullException(nameof(idSelector));

        var clone = Clone();
        clone.IdSelector = idSelector;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithIdToStreamKeySelector(
        Func<string, string> idToStreamKeySelector)
    {
        if (idToStreamKeySelector == null) throw new ArgumentNullException(nameof(idToStreamKeySelector));

        var clone = Clone();
        clone.IdToStreamKeySelector = idToStreamKeySelector;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithIdToSnapshotKeySelector(
        Func<string, string> idToSnapshotKeySelector)
    {
        if (idToSnapshotKeySelector == null) throw new ArgumentNullException(nameof(idToSnapshotKeySelector));

        var clone = Clone();
        clone.IdToSnapshotKeySelector = idToSnapshotKeySelector;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithEventApplier(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));

        var clone = Clone();
        clone.EventApplier = (agg, e) => eventApplier(agg, (TEventBase)e);
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithCommandResultOptions(
        ICommandResultOptions commandResultOptions)
    {
        if (commandResultOptions == null) throw new ArgumentNullException(nameof(commandResultOptions));

        var clone = Clone();
        clone._commandResultsOptions[commandResultOptions.ClrType] = commandResultOptions;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithCommandResultsOptions(
        IEnumerable<ICommandResultOptions> commandResultsOptions)
    {
        if (commandResultsOptions == null) throw new ArgumentNullException(nameof(commandResultsOptions));

        var clone = Clone();

        foreach (var commandResultOptions in commandResultsOptions)
        {
            clone._commandResultsOptions[commandResultOptions.ClrType] = commandResultOptions;
        }

        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithEventsOptions(IEnumerable<IEventOptions> eventsOptions)
    {
        if (eventsOptions == null) throw new ArgumentNullException(nameof(eventsOptions));

        var clone = Clone();

        foreach (var eventOptions in eventsOptions)
        {
            clone._eventsOptions[eventOptions.ClrType] = eventOptions;
        }

        return clone;
    }

    public bool HasCommandResultOptions<TCommandResult>()
    {
        return _commandResultsOptions.ContainsKey(typeof(TCommandResult));
    }

    protected abstract AggregateOptionsBase<TAggregate, TEventBase> Clone();

    protected ICommandResultOptions GetCommandResultOptions<TCommandResult>()
    {
        var commandResultClrType = typeof(TCommandResult);

        if (_commandResultsOptions.TryGetValue(commandResultClrType, out var commandResultOptions))
        {
            return commandResultOptions;
        }

        throw new KeyNotFoundException($"No command result options found for CLR type {commandResultClrType.Name}.");
    }
}