using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public abstract class AggregateTypeBase<TAggregate, TEventBase> : IAggregateType<TAggregate>
{
    private readonly Func<TAggregate> _factory;
    private readonly Func<TAggregate, string> _idSelector;
    private readonly Func<string, string> _idToStreamKeySelector;
    private readonly Func<string, string> _idToSnapshotKeySelector;
    private readonly IReadOnlyDictionary<Type, IEventType> _eventTypes;

    protected AggregateTypeBase(
        Func<TAggregate> factory,
        Func<TAggregate, string> idSelector,
        Func<string, string> idToStreamKeySelector,
        Func<string, string> idToSnapshotKeySelector,
        IEnumerable<ICommandReturnType> commandReturnTypes,
        IEnumerable<IEventType> eventTypes)
    {
        _factory = factory;
        _idSelector = idSelector;
        _idToStreamKeySelector = idToStreamKeySelector;
        _idToSnapshotKeySelector = idToSnapshotKeySelector;
        CommandReturnTypes = commandReturnTypes.ToDictionary(x => x.ClrType);
        _eventTypes = eventTypes.ToDictionary(x => x.ClrType);
    }
    
    public Type ClrType => typeof(TAggregate);
    public Type EventBaseType => typeof(TEventBase);
    public IEnumerable<IEventType> EventTypes => _eventTypes.Values;
    protected IReadOnlyDictionary<Type, ICommandReturnType> CommandReturnTypes { get; }

    public TAggregate CreateNew() => _factory();
    public string SelectId(TAggregate aggregate) => _idSelector(aggregate);
    public string SelectStreamKeyFromId(string id) => _idToStreamKeySelector(id);
    public string SelectSnapshotKeyFromId(string id) => _idToSnapshotKeySelector(id);
    public string SelectStreamKey(TAggregate aggregate) => _idToStreamKeySelector(SelectId(aggregate));
    public string SelectSnapshotKey(TAggregate aggregate) => _idToSnapshotKeySelector(SelectId(aggregate));

    public abstract TAggregate ApplyEvent(TAggregate aggregate, object @event);
    public abstract ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate);
}