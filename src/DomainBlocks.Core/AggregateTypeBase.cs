namespace DomainBlocks.Core;

public abstract class AggregateTypeBase<TAggregate, TEventBase> :
    EventSourcedEntityTypeBase<TAggregate>,
    IAggregateType<TAggregate>
{
    private readonly Dictionary<Type, ICommandResultType> _commandResultTypes = new();
    private readonly Dictionary<Type, AggregateEventType<TAggregate>> _eventTypes = new();
    private Func<TAggregate, object, TAggregate>? _eventApplier;

    protected AggregateTypeBase()
    {
    }

    protected AggregateTypeBase(AggregateTypeBase<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
        if (copyFrom == null) throw new ArgumentNullException(nameof(copyFrom));

        _eventApplier = copyFrom._eventApplier;
        _commandResultTypes = new Dictionary<Type, ICommandResultType>(copyFrom._commandResultTypes);
        _eventTypes = new Dictionary<Type, AggregateEventType<TAggregate>>(copyFrom._eventTypes);
    }

    public override IEnumerable<IEventType> EventTypes => _eventTypes.Values;

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

    public AggregateTypeBase<TAggregate, TEventBase> SetEventApplier(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));

        var clone = CloneImpl();
        clone._eventApplier = (agg, e) => eventApplier(agg, (TEventBase)e);
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetCommandResultType(ICommandResultType commandResultType)
    {
        if (commandResultType == null) throw new ArgumentNullException(nameof(commandResultType));

        var clone = CloneImpl();
        clone._commandResultTypes[commandResultType.ClrType] = commandResultType;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> SetCommandResultTypes(
        IEnumerable<ICommandResultType> commandResultTypes)
    {
        if (commandResultTypes == null) throw new ArgumentNullException(nameof(commandResultTypes));

        var clone = CloneImpl();

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

        var clone = CloneImpl();

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

    protected ICommandResultType GetCommandResultType<TCommandResult>()
    {
        var commandResultClrType = typeof(TCommandResult);

        if (_commandResultTypes.TryGetValue(commandResultClrType, out var commandResultType))
        {
            return commandResultType;
        }

        throw new KeyNotFoundException($"No command result type found for CLR type {commandResultClrType.Name}.");
    }

    private AggregateTypeBase<TAggregate, TEventBase> CloneImpl() => (AggregateTypeBase<TAggregate, TEventBase>)Clone();
}