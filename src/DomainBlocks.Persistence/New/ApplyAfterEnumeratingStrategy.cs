using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Persistence.New;

public static class ApplyAfterEnumeratingStrategy
{
    public static ICommandResultStrategy<TAggregate, TEventBase, TCommandResult> Create<
        TAggregate, TEventBase, TCommandResult>(
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        return new ApplyAfterEnumeratingStrategy<TAggregate, TEventBase, TCommandResult>(
            eventsSelector, eventApplier);
    }
}

public class ApplyAfterEnumeratingStrategy<TAggregate, TEventBase, TCommandResult>
    : ICommandResultStrategy<TAggregate, TEventBase, TCommandResult>
{
    private readonly Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> _eventsSelector;
    private readonly Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public ApplyAfterEnumeratingStrategy(
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _eventsSelector = eventsSelector ?? throw new ArgumentNullException(nameof(eventsSelector));
        _eventApplier = eventApplier ?? throw new ArgumentNullException(nameof(eventApplier));
    }

    public (TAggregate, IEnumerable<TEventBase>) GetUpdatedStateAndEvents(
        TCommandResult commandResult, TAggregate aggregate)
    {
        var events = _eventsSelector(commandResult, aggregate).ToList();
        var updatedState = events.Aggregate(aggregate, _eventApplier);
        return (updatedState, events);
    }
}