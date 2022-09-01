using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public class CommandResultType<TAggregate, TEventBase, TCommandResult> : ICommandResultType
{
    private readonly Func<TAggregate, TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private readonly Func<TAggregate, TCommandResult, TAggregate> _updatedStateSelector;

    public CommandResultType(
        Func<TAggregate, TCommandResult, IEnumerable<TEventBase>> eventsSelector,
        Func<TAggregate, TCommandResult, TAggregate> updatedStateSelector)
    {
        _eventsSelector = eventsSelector;
        _updatedStateSelector = updatedStateSelector;
    }

    public Type ClrType => typeof(TCommandResult);

    public IEnumerable<TEventBase> SelectEvents(TAggregate aggregate, TCommandResult commandResult)
    {
        return _eventsSelector(aggregate, commandResult);
    }

    public TAggregate SelectUpdatedState(TAggregate aggregate, TCommandResult commandResult)
    {
        return _updatedStateSelector(aggregate, commandResult);
    }
}