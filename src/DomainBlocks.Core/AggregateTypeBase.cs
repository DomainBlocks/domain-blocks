using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public abstract class AggregateTypeBase<TAggregate, TEventBase> : IAggregateType<TAggregate>
{
    private Dictionary<Type, ICommandReturnType> _commandReturnTypes = new();
    private Dictionary<Type, IEventType> _eventTypes = new();

    protected AggregateTypeBase()
    {
    }

    protected AggregateTypeBase(AggregateTypeBase<TAggregate, TEventBase> copyFrom)
    {
        Factory = copyFrom.Factory;
        IdSelector = copyFrom.IdSelector;
        IdToStreamKeySelector = copyFrom.IdToStreamKeySelector;
        IdToSnapshotKeySelector = copyFrom.IdToSnapshotKeySelector;
        EventApplier = copyFrom.EventApplier;
        _commandReturnTypes = new Dictionary<Type, ICommandReturnType>(copyFrom._commandReturnTypes);
        _eventTypes = new Dictionary<Type, IEventType>(copyFrom._eventTypes);
    }

    public Type ClrType => typeof(TAggregate);
    public Type EventBaseType => typeof(TEventBase);
    public IEnumerable<IEventType> EventTypes => _eventTypes.Values;

    public Func<TAggregate> Factory { get; private set; }
    public Func<TAggregate, string> IdSelector { get; private set; }
    public Func<string, string> IdToStreamKeySelector { get; private set; }
    public Func<string, string> IdToSnapshotKeySelector { get; private set; }
    public Func<TAggregate, object, TAggregate> EventApplier { get; private set; }
    
    public string SelectStreamKey(TAggregate aggregate) => IdToStreamKeySelector(IdSelector(aggregate));
    public string SelectSnapshotKey(TAggregate aggregate) => IdToSnapshotKeySelector(IdSelector(aggregate));

    public abstract ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate);

    public AggregateTypeBase<TAggregate, TEventBase> WithFactory(Func<TAggregate> factory)
    {
        var clone = Clone();
        clone.Factory = factory;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithIdSelector(Func<TAggregate, string> idSelector)
    {
        var clone = Clone();
        clone.IdSelector = idSelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithIdToStreamKeySelector(
        Func<string, string> idToStreamKeySelector)
    {
        var clone = Clone();
        clone.IdToStreamKeySelector = idToStreamKeySelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithIdToSnapshotKeySelector(
        Func<string, string> idToSnapshotKeySelector)
    {
        var clone = Clone();
        clone.IdToSnapshotKeySelector = idToSnapshotKeySelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithEventApplier(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        var clone = Clone();
        clone.EventApplier = (agg, e) => eventApplier(agg, (TEventBase)e);
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithCommandReturnTypes(
        IEnumerable<ICommandReturnType> commandReturnTypes)
    {
        var clone = Clone();
        clone._commandReturnTypes = commandReturnTypes.ToDictionary(x => x.ClrType);
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithEventTypes(IEnumerable<IEventType> eventTypeOptions)
    {
        var clone = Clone();
        clone._eventTypes = eventTypeOptions.ToDictionary(x => x.ClrType);
        return clone;
    }

    protected abstract AggregateTypeBase<TAggregate, TEventBase> Clone();

    protected ICommandReturnType GetCommandReturnType<TCommandResult>()
    {
        var commandReturnClrType = typeof(TCommandResult);

        if (_commandReturnTypes.TryGetValue(commandReturnClrType, out var commandReturnType))
        {
            return commandReturnType;
        }

        throw new KeyNotFoundException(
            $"No command return type options found for CLR type {commandReturnClrType.Name}.");
    }
}