using System.Linq.Expressions;

namespace DomainBlocks.Core;

public abstract class AggregateOptionsBase<TAggregate, TEventBase> : IAggregateOptions<TAggregate>
{
    private static readonly Lazy<Func<TAggregate>> DefaultFactory = new(() => GetDefaultFactory());
    private static readonly Lazy<Func<TAggregate, string>> DefaultIdSelector = new(GetDefaultIdSelector);
    private readonly Dictionary<Type, ICommandResultOptions> _commandResultsOptions = new();
    private readonly Dictionary<Type, AggregateEventType<TAggregate>> _eventTypes = new();
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private Func<TAggregate, object, TAggregate>? _eventApplier;

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
        _eventTypes = new Dictionary<Type, AggregateEventType<TAggregate>>(copyFrom._eventTypes);
    }

    public Type ClrType => typeof(TAggregate);
    public IEnumerable<IEventType> EventTypes => _eventTypes.Values;

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

        if (_eventTypes.TryGetValue(@event.GetType(), out var eventType) && eventType.HasEventApplier)
        {
            return eventType.InvokeEventApplier(aggregate, (TEventBase)@event);
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

    internal AggregateOptionsBase<TAggregate, TEventBase> WithEventTypes(
        IEnumerable<AggregateEventType<TAggregate>> eventTypes)
    {
        if (eventTypes == null) throw new ArgumentNullException(nameof(eventTypes));

        var clone = Clone();

        foreach (var eventType in eventTypes)
        {
            if (clone._eventTypes.TryGetValue(eventType.ClrType, out var existingEventType))
            {
                clone._eventTypes[eventType.ClrType] = existingEventType.Merge(eventType);
            }
            else
            {
                clone._eventTypes.Add(eventType.ClrType, eventType);
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
            return () => throw new InvalidOperationException(
                "No factory function specified, and no default constructor found");
        }

        var newExpr = Expression.New(ctor);
        var lambda = Expression.Lambda<Func<TAggregate>>(newExpr);
        return lambda.Compile();
    }

    private static Func<TAggregate, string> GetDefaultIdSelector()
    {
        const string defaultIdPropertyName = "Id";

        var idSelector =
            (GetIdSelector(defaultIdPropertyName) ??
             GetIdSelector($"{typeof(TAggregate).Name}{defaultIdPropertyName}")) ??
            (_ => throw new InvalidOperationException("No ID selector specified, and no suitable ID property found"));

        return idSelector;
    }

    private static Func<TAggregate, string>? GetIdSelector(string propertyName)
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