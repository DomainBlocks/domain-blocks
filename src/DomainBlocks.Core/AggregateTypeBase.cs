using System.Linq.Expressions;

namespace DomainBlocks.Core;

public abstract class AggregateTypeBase<TAggregate, TEventBase> : IAggregateType<TAggregate>
{
    private static readonly Lazy<Func<TAggregate>> DefaultFactory = new(() => GetDefaultFactory());
    private static readonly Lazy<Func<TAggregate, string>> DefaultIdSelector = new(GetDefaultIdSelector);
    private readonly Dictionary<Type, ICommandResultType> _commandResultTypes = new();
    private readonly Dictionary<Type, AggregateEventType<TAggregate>> _eventTypes = new();
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private Func<TAggregate, object, TAggregate>? _eventApplier;

    protected AggregateTypeBase()
    {
        _factory = DefaultFactory.Value;
        _idSelector = DefaultIdSelector.Value;
        var prefix = GetDefaultKeyPrefix();
        _idToStreamKeySelector = GetIdToStreamKeySelector(prefix);
        _idToSnapshotKeySelector = GetIdToSnapshotKeySelector(prefix);
    }

    protected AggregateTypeBase(AggregateTypeBase<TAggregate, TEventBase> copyFrom)
    {
        if (copyFrom == null) throw new ArgumentNullException(nameof(copyFrom));

        _eventApplier = copyFrom._eventApplier;
        _factory = copyFrom._factory;
        _idSelector = copyFrom._idSelector;
        _idToStreamKeySelector = copyFrom._idToStreamKeySelector;
        _idToSnapshotKeySelector = copyFrom._idToSnapshotKeySelector;
        _commandResultTypes = new Dictionary<Type, ICommandResultType>(copyFrom._commandResultTypes);
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

    public TAggregate InvokeEventApplier(TAggregate aggregate, object @event)
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

    public AggregateTypeBase<TAggregate, TEventBase> SetFactory(Func<TAggregate> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var clone = Clone();
        clone._factory = factory;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetIdSelector(Func<TAggregate, string> idSelector)
    {
        if (idSelector == null) throw new ArgumentNullException(nameof(idSelector));

        var clone = Clone();
        clone._idSelector = idSelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetKeyPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix cannot be null or whitespace.", nameof(prefix));

        var clone = Clone();
        clone._idToStreamKeySelector = GetIdToStreamKeySelector(prefix);
        clone._idToSnapshotKeySelector = GetIdToSnapshotKeySelector(prefix);
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetIdToStreamKeySelector(
        Func<string, string> idToStreamKeySelector)
    {
        if (idToStreamKeySelector == null) throw new ArgumentNullException(nameof(idToStreamKeySelector));

        var clone = Clone();
        clone._idToStreamKeySelector = idToStreamKeySelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetIdToSnapshotKeySelector(
        Func<string, string> idToSnapshotKeySelector)
    {
        if (idToSnapshotKeySelector == null) throw new ArgumentNullException(nameof(idToSnapshotKeySelector));

        var clone = Clone();
        clone._idToSnapshotKeySelector = idToSnapshotKeySelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetEventApplier(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));

        var clone = Clone();
        clone._eventApplier = (agg, e) => eventApplier(agg, (TEventBase)e);
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetCommandResultType(ICommandResultType commandResultType)
    {
        if (commandResultType == null) throw new ArgumentNullException(nameof(commandResultType));

        var clone = Clone();
        clone._commandResultTypes[commandResultType.ClrType] = commandResultType;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetCommandResultTypes(
        IEnumerable<ICommandResultType> commandResultTypes)
    {
        if (commandResultTypes == null) throw new ArgumentNullException(nameof(commandResultTypes));

        var clone = Clone();

        foreach (var commandResultType in commandResultTypes)
        {
            clone._commandResultTypes[commandResultType.ClrType] = commandResultType;
        }

        return clone;
    }

    internal AggregateTypeBase<TAggregate, TEventBase> SetEventTypes(
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

    public bool HasCommandResultType<TCommandResult>()
    {
        return _commandResultTypes.ContainsKey(typeof(TCommandResult));
    }

    protected abstract AggregateTypeBase<TAggregate, TEventBase> Clone();

    protected ICommandResultType GetCommandResultType<TCommandResult>()
    {
        var commandResultClrType = typeof(TCommandResult);

        if (_commandResultTypes.TryGetValue(commandResultClrType, out var commandResultType))
        {
            return commandResultType;
        }

        throw new KeyNotFoundException($"No command result type found for CLR type {commandResultClrType.Name}.");
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