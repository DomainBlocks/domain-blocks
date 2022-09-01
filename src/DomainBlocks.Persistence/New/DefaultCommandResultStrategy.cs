using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public static class DefaultCommandResultStrategy
{
    public static DefaultCommandResultStrategy<TAggregate, TEventBase, TCommandResult> Create<
        TAggregate, TEventBase, TCommandResult>(
        Func<TCommandResult, TAggregate, TAggregate> updatedStateSelector,
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        return new DefaultCommandResultStrategy<TAggregate, TEventBase, TCommandResult>(
            updatedStateSelector, eventsSelector);
    }
}

public class DefaultCommandResultStrategy<TAggregate, TEventBase, TCommandResult>
    : ICommandResultStrategy<TAggregate, TEventBase, TCommandResult>
{
    private readonly Func<TCommandResult, TAggregate, TAggregate> _updatedStateSelector;
    private readonly Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> _eventsSelector;

    public DefaultCommandResultStrategy(
        Func<TCommandResult, TAggregate, TAggregate> updatedStateSelector,
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector)
    {
        _updatedStateSelector = updatedStateSelector ?? throw new ArgumentNullException(nameof(updatedStateSelector));
        _eventsSelector = eventsSelector ?? throw new ArgumentNullException(nameof(eventsSelector));
    }

    public (TAggregate, IEnumerable<TEventBase>) GetUpdatedStateAndEvents(
        TCommandResult commandResult, TAggregate aggregate)
    {
        var updatedState = _updatedStateSelector(commandResult, aggregate);
        var events = _eventsSelector(commandResult, aggregate);
        return (updatedState, events);
    }
}