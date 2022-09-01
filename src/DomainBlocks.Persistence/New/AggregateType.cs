using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New;

public class AggregateType<TAggregate, TEventBase> : IAggregateType
{
    private readonly Func<TAggregate> _factory;
    private readonly Func<TAggregate, string> _idSelector;
    // TODO: Add other key selectors here
    private readonly IReadOnlyDictionary<Type, ICommandResultType> _commandResultTypes;
    private readonly Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public AggregateType(
        Func<TAggregate> factory,
        Func<TAggregate, string> idSelector,
        IEnumerable<ICommandResultType> commandResultTypes,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _factory = factory;
        _idSelector = idSelector;
        _commandResultTypes = commandResultTypes.ToDictionary(x => x.ClrType);
        _eventApplier = eventApplier;
    }

    public Type ClrType => typeof(TAggregate);
    public Type EventBaseType => typeof(TEventBase);

    public TAggregate CreateNew() => _factory();

    public string SelectId(TAggregate aggregate) => _idSelector(aggregate);

    public CommandResultType<TAggregate, TEventBase, TCommandResult> GetCommandResultType<TCommandResult>()
    {
        return (CommandResultType<TAggregate, TEventBase, TCommandResult>)_commandResultTypes[typeof(TCommandResult)];
    }

    public TAggregate ApplyEvent(TAggregate aggregate, TEventBase @event) => _eventApplier(aggregate, @event);
}