using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public abstract class AggregateTypeBase<TAggregate, TEventBase> : IAggregateType<TAggregate>
{
    private Func<TAggregate> _factory;
    private Func<TAggregate, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;
    private Dictionary<Type, ICommandReturnType> _commandReturnTypes = new();
    private Dictionary<Type, IEventType> _eventTypes = new();

    protected AggregateTypeBase()
    {
    }

    protected AggregateTypeBase(AggregateTypeBase<TAggregate, TEventBase> copyFrom)
    {
        _factory = copyFrom._factory;
        _idSelector = copyFrom._idSelector;
        _idToStreamKeySelector = copyFrom._idToStreamKeySelector;
        _idToSnapshotKeySelector = copyFrom._idToSnapshotKeySelector;
        _commandReturnTypes = new Dictionary<Type, ICommandReturnType>(copyFrom._commandReturnTypes);
        _eventTypes = new Dictionary<Type, IEventType>(copyFrom._eventTypes);
    }

    public Type ClrType => typeof(TAggregate);
    public Type EventBaseType => typeof(TEventBase);
    public IEnumerable<IEventType> EventTypes => _eventTypes.Values;

    public TAggregate CreateNew() => _factory();
    public string SelectId(TAggregate aggregate) => _idSelector(aggregate);
    public string SelectStreamKeyFromId(string id) => _idToStreamKeySelector(id);
    public string SelectSnapshotKeyFromId(string id) => _idToSnapshotKeySelector(id);
    public string SelectStreamKey(TAggregate aggregate) => _idToStreamKeySelector(SelectId(aggregate));
    public string SelectSnapshotKey(TAggregate aggregate) => _idToSnapshotKeySelector(SelectId(aggregate));

    public abstract TAggregate ApplyEvent(TAggregate aggregate, object @event);
    public abstract ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate);

    public AggregateTypeBase<TAggregate, TEventBase> WithFactory(Func<TAggregate> factory)
    {
        var clone = Clone();
        clone._factory = factory;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithIdSelector(Func<TAggregate, string> idSelector)
    {
        var clone = Clone();
        clone._idSelector = idSelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithIdToStreamKeySelector(
        Func<string, string> idToStreamKeySelector)
    {
        var clone = Clone();
        clone._idToStreamKeySelector = idToStreamKeySelector;
        return clone;
    }

    public AggregateTypeBase<TAggregate, TEventBase> WithIdToSnapshotKeySelector(
        Func<string, string> idToSnapshotKeySelector)
    {
        var clone = Clone();
        clone._idToSnapshotKeySelector = idToSnapshotKeySelector;
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