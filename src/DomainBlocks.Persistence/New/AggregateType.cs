using System;
using System.Collections.Generic;
using System.Linq;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence.New;

public class AggregateType<TAggregate, TEventBase> : IAggregateType
{
    private readonly Func<TAggregate> _factory;
    private readonly Func<TAggregate, string> _idSelector;
    private readonly Func<string, string> _idToStreamKeySelector;
    private readonly Func<string, string> _idToSnapshotKeySelector;
    private readonly IReadOnlyDictionary<Type, ICommandResultType> _commandResultTypes;
    private readonly Func<TAggregate, TEventBase, TAggregate> _eventApplier;
    
    public AggregateType(Func<TAggregate> factory,
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
        _eventApplier = eventApplier;

        // Create event name map from event types.
        EventNameMap = new EventNameMap();
        foreach (var eventType in eventTypes)
        {
            EventNameMap.Add(eventType.EventName, eventType.ClrType);
        }
    }

    public Type ClrType => typeof(TAggregate);
    public Type EventBaseType => typeof(TEventBase);
    public EventNameMap EventNameMap { get; }

    public TAggregate CreateNew() => _factory();
    public string SelectId(TAggregate aggregate) => _idSelector(aggregate);
    public string SelectStreamKey(TAggregate aggregate) => _idToStreamKeySelector(SelectId(aggregate));
    public string SelectSnapshotKey(TAggregate aggregate) => _idToSnapshotKeySelector(SelectId(aggregate));

    public CommandResultType<TAggregate, TEventBase, TCommandResult> GetCommandResultType<TCommandResult>()
    {
        return (CommandResultType<TAggregate, TEventBase, TCommandResult>)_commandResultTypes[typeof(TCommandResult)];
    }

    public TAggregate ApplyEvent(TAggregate aggregate, TEventBase @event) => _eventApplier(aggregate, @event);
}