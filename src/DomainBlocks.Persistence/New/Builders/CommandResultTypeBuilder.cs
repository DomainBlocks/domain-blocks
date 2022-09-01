using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New.Builders;

public class CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> : ICommandResultTypeBuilder
{
    private Func<TAggregate, TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private Func<TAggregate, TCommandResult, TAggregate> _updatedStateSelector;

    public CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> WithEventsFrom(
        Func<TAggregate, TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector;
        return this;
    }

    public CommandResultTypeBuilder<TAggregate, TEventBase, TCommandResult> WithUpdatedStateFrom(
        Func<TAggregate, TCommandResult, TAggregate> updatedStateSelector)
    {
        _updatedStateSelector = updatedStateSelector;
        return this;
    }

    public ICommandResultType Build()
    {
        return new CommandResultType<TAggregate, TEventBase, TCommandResult>(
            _eventsSelector, _updatedStateSelector);
    }
}