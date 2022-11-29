using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace DomainBlocks.Core;

public abstract class AggregateOptionsBase<TAggregate, TEventBase> : IAggregateOptions<TAggregate>
{
    private static readonly Lazy<Func<TAggregate>> DefaultFactory = new(() => GetDefaultFactory());
    private static readonly Lazy<Func<TAggregate, string>> DefaultIdSelector = new(GetDefaultIdSelector);
    private readonly Dictionary<Type, ICommandResultOptions> _commandResultsOptions = new();
    private readonly Dictionary<Type, EventOptions<TAggregate>> _eventsOptions = new();
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private Func<TAggregate, object, TAggregate> _eventApplier;

    protected AggregateOptionsBase()
    {
        _factory = DefaultFactory.Value;
        _idSelector = DefaultIdSelector.Value;
        var prefix = GetDefaultKeyPrefix();
        _idToStreamKeySelector = GetIdToStreamKeySelector(prefix);
        _idToSnapshotKeySelector = GetIdToSnapshotKeySelector(prefix);
    }

    protected AggregateOptionsBase(AggregateOptionsBase<TAggregate, TEventBase> copyFrom)
    {
        if (copyFrom == null) throw new ArgumentNullException(nameof(copyFrom));

        _eventApplier = copyFrom._eventApplier;
        _factory = copyFrom._factory;
        _idSelector = copyFrom._idSelector;
        _idToStreamKeySelector = copyFrom._idToStreamKeySelector;
        _idToSnapshotKeySelector = copyFrom._idToSnapshotKeySelector;
        _commandResultsOptions = new Dictionary<Type, ICommandResultOptions>(copyFrom._commandResultsOptions);
        _eventsOptions = new Dictionary<Type, EventOptions<TAggregate>>(copyFrom._eventsOptions);
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

    public string GetId(TAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));

        if (_idSelector == null)
        {
            throw new InvalidOperationException("Cannot get ID as no ID selector has been specified.");
        }

        return _idSelector(aggregate);
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
        return MakeSnapshotKey(GetId(aggregate));
    }

    public abstract ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);

    public TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        if (_eventsOptions.TryGetValue(@event.GetType(), out var eventOptions) && eventOptions.HasEventApplier)
        {
            return eventOptions.ApplyEvent(aggregate, (TEventBase)@event);
        }

        if (_eventApplier != null)
        {
            return _eventApplier(aggregate, @event);
        }

        throw new InvalidOperationException(
            $"Unable to apply event {@event.GetType().Name} to aggregate {typeof(TAggregate).Name} as no event " +
            "applier was found.");
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

    public AggregateOptionsBase<TAggregate, TEventBase> WithKeyPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix cannot be null or whitespace.", nameof(prefix));

        var clone = Clone();
        clone._idToStreamKeySelector = GetIdToStreamKeySelector(prefix);
        clone._idToSnapshotKeySelector = GetIdToSnapshotKeySelector(prefix);
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

    internal AggregateOptionsBase<TAggregate, TEventBase> WithEventsOptions(
        IEnumerable<EventOptions<TAggregate>> eventsOptions)
    {
        if (eventsOptions == null) throw new ArgumentNullException(nameof(eventsOptions));

        var clone = Clone();

        foreach (var eventOptions in eventsOptions)
        {
            if (clone._eventsOptions.TryGetValue(eventOptions.ClrType, out var existingOptions))
            {
                clone._eventsOptions[eventOptions.ClrType] = existingOptions.Merge(eventOptions);
            }
            else
            {
                clone._eventsOptions.Add(eventOptions.ClrType, eventOptions);
            }
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

    private static Func<TAggregate> GetDefaultFactory()
    {
        var ctor = typeof(TAggregate).GetConstructor(Type.EmptyTypes);
        if (ctor == null)
        {
            return null;
        }

        var newExpr = Expression.New(ctor);
        var lambda = Expression.Lambda<Func<TAggregate>>(newExpr);
        return lambda.Compile();
    }

    private static Func<TAggregate, string> GetDefaultIdSelector()
    {
        const string defaultIdPropertyName = "Id";

        return GetIdSelector(defaultIdPropertyName) ??
               GetIdSelector($"{typeof(TAggregate).Name}{defaultIdPropertyName}");
    }

    private static Func<TAggregate, string> GetIdSelector(string propertyName)
    {
        var property = typeof(TAggregate).GetProperty(propertyName);
        if (property == null)
        {
            return null;
        }

        var aggregateParam = Expression.Parameter(typeof(TAggregate));
        var propertyExpr = Expression.Property(aggregateParam, property);
        var asString = Expression.Call(propertyExpr, nameof(ToString), null);
        var lambda = Expression.Lambda<Func<TAggregate, string>>(asString, aggregateParam);
        return lambda.Compile();
    }

    private static string GetDefaultKeyPrefix()
    {
        var name = typeof(TAggregate).Name;
        return $"{name[..1].ToLower()}{name[1..]}";
    }

    private static Func<string, string> GetIdToStreamKeySelector(string prefix)
    {
        return id => $"{prefix}-{id}";
    }

    private static Func<string, string> GetIdToSnapshotKeySelector(string prefix)
    {
        return id => $"{prefix}Snapshot-{id}";
    }
}