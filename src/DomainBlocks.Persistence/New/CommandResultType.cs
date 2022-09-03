using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public class CommandResultType<TAggregate, TEventBase, TCommandResult>
    : ICommandResultType<TAggregate, TCommandResult> where TEventBase : class
{
    private readonly ICommandResultStrategy<TAggregate, TEventBase, TCommandResult> _strategy;

    public CommandResultType(ICommandResultStrategy<TAggregate, TEventBase, TCommandResult> strategy)
    {
        _strategy = strategy;
    }

    public Type ClrType => typeof(TCommandResult);

    public (TAggregate, IEnumerable<TEventBase>) GetUpdatedStateAndEvents(
        TCommandResult commandResult, TAggregate aggregate)
    {
        return _strategy.GetUpdatedStateAndEvents(commandResult, aggregate);
    }

    (TAggregate, IEnumerable<object>) ICommandResultType<TAggregate, TCommandResult>.GetUpdatedStateAndEvents(
        TCommandResult commandResult, TAggregate aggregate)
    {
        return GetUpdatedStateAndEvents(commandResult, aggregate);
    }
}