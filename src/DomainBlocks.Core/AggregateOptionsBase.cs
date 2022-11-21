using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public abstract class AggregateOptionsBase<TAggregate, TEventBase> : IAggregateOptions<TAggregate>
{
    private readonly Dictionary<Type, ICommandResultOptions> _commandResultsOptions = new();
    private readonly Dictionary<Type, IEventOptions> _eventsOptions = new();
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private Func<TAggregate, object, TAggregate> _eventApplier;

    protected AggregateOptionsBase()
    {
        _idToStreamKeySelector = GetDefaultIdToStreamKeySelector();
        _idToSnapshotKeySelector = GetDefaultIdToSnapshotKeySelector();
    }

    protected AggregateOptionsBase(AggregateOptionsBase<TAggregate, TEventBase> copyFrom)
    {
        if (copyFrom == null) throw new ArgumentNullException(nameof(copyFrom));

        _eventApplier = copyFrom._eventApplier;
        _factory = copyFrom._factory;
        _idSelector = copyFrom._idSelector;
        _idToStreamKeySelector = copyFrom._idToStreamKeySelector ?? GetDefaultIdToStreamKeySelector();
        _idToSnapshotKeySelector = copyFrom._idToSnapshotKeySelector ?? GetDefaultIdToSnapshotKeySelector();
        _commandResultsOptions = new Dictionary<Type, ICommandResultOptions>(copyFrom._commandResultsOptions);
        _eventsOptions = new Dictionary<Type, IEventOptions>(copyFrom._eventsOptions);
    }

    public Type ClrType => typeof(TAggregate);
    public IEnumerable<IEventOptions> EventsOptions => _eventsOptions.Values;

    public TAggregate CreateNew()
    {
        if (_factory == null)
        {
            throw new InvalidOperationException(
                "Cannot create new aggregate instance as no factory has been specified.");
        }

        return _factory();
    }

    public string MakeStreamKey(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("ID cannot be null or whitespace", nameof(id));

        return _idToStreamKeySelector(id);
    }

    public string MakeSnapshotKey(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("ID cannot be null or whitespace", nameof(id));

        return _idToSnapshotKeySelector(id);
    }

    public string MakeSnapshotKey(TAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));

        if (_idSelector == null)
        {
            throw new InvalidOperationException("Cannot make snapshot key as no ID selector has been specified.");
        }

        return MakeSnapshotKey(_idSelector(aggregate));
    }

    public abstract ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);

    public TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        if (_eventApplier == null)
        {
            throw new InvalidOperationException(
                "Cannot apply event to aggregate as no event applier has been specified.");
        }

        return _eventApplier(aggregate, @event);
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithFactory(Func<TAggregate> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var clone = Clone();
        clone._factory = factory;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithIdSelector(Func<TAggregate, string> idSelector)
    {
        if (idSelector == null) throw new ArgumentNullException(nameof(idSelector));

        var clone = Clone();
        clone._idSelector = idSelector;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithIdToStreamKeySelector(
        Func<string, string> idToStreamKeySelector)
    {
        if (idToStreamKeySelector == null) throw new ArgumentNullException(nameof(idToStreamKeySelector));

        var clone = Clone();
        clone._idToStreamKeySelector = idToStreamKeySelector;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithIdToSnapshotKeySelector(
        Func<string, string> idToSnapshotKeySelector)
    {
        if (idToSnapshotKeySelector == null) throw new ArgumentNullException(nameof(idToSnapshotKeySelector));

        var clone = Clone();
        clone._idToSnapshotKeySelector = idToSnapshotKeySelector;
        return clone;
    }

    public AggregateOptionsBase<TAggregate, TEventBase> WithEventApplier(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));

        var clone = Clone();
        clone._eventApplier = (agg, e) => eventApplier(agg, (TEventBase)e);
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

    private static Func<string, string> GetDefaultIdToStreamKeySelector()
    {
        var name = typeof(TAggregate).Name;
        name = $"{name[..1].ToLower()}{name[1..]}";
        return id => $"{name}-{id}";
    }

    private static Func<string, string> GetDefaultIdToSnapshotKeySelector()
    {
        var name = typeof(TAggregate).Name;
        name = $"{name[..1].ToLower()}{name[1..]}Snapshot";
        return id => $"{name}-{id}";
    }
}