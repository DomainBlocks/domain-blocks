using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New;

public class AggregateType<TAggregate, TEventBase> : IAggregateType
{
    private readonly Func<TAggregate> _factory;
    private readonly Func<TAggregate, string> _idSelector;
    private readonly Func<string, string> _idToStreamKeySelector;
    private readonly Func<string, string> _idToSnapshotKeySelector;
    private readonly IReadOnlyDictionary<Type, ICommandResultType> _commandResultTypes;
    private readonly IReadOnlyDictionary<Type, IEventType> _eventTypes;
    private readonly Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public AggregateType(
        Func<TAggregate> factory,
        Func<TAggregate, string> idSelector,
        Func<string, string> idToStreamKeySelector,
        Func<string, string> idToSnapshotKeySelector,
        IEnumerable<ICommandResultType> commandResultTypes,
        IEnumerable<IEventType> eventTypes,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _factory = factory;
        _idSelector = idSelector;
        _idToStreamKeySelector = idToStreamKeySelector;
        _idToSnapshotKeySelector = idToSnapshotKeySelector;
        _commandResultTypes = commandResultTypes.ToDictionary(x => x.ClrType);
        _eventTypes = eventTypes.ToDictionary(x => x.ClrType);
        _eventApplier = eventApplier;
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

    public CommandResultType<TAggregate, TEventBase, TCommandResult> GetCommandResultType<TCommandResult>()
    {
        return (CommandResultType<TAggregate, TEventBase, TCommandResult>)_commandResultTypes[typeof(TCommandResult)];
    }

    public VoidCommandResultType<TAggregate, TEventBase> GetVoidCommandResultType()
    {
        return (VoidCommandResultType<TAggregate, TEventBase>)_commandResultTypes[typeof(void)];
    }

    public TAggregate ApplyEvent(TAggregate aggregate, TEventBase @event) => _eventApplier(aggregate, @event);
}