using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public static class ApplyWhileEnumeratingStrategy
{
    public static ICommandResultStrategy<TAggregate, TEventBase, TCommandResult> Create<
        TAggregate, TEventBase, TCommandResult>(
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        return new ApplyEventsCommandResultStrategy<TAggregate, TEventBase, TCommandResult>(
            eventsSelector, eventApplier);
    }
}

public class ApplyEventsCommandResultStrategy<TAggregate, TEventBase, TCommandResult>
    : ICommandResultStrategy<TAggregate, TEventBase, TCommandResult>
{
    private readonly Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> _eventsSelector;
    private readonly Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public ApplyEventsCommandResultStrategy(
        Func<TCommandResult, TAggregate, IEnumerable<TEventBase>> eventsSelector,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _eventsSelector = eventsSelector ?? throw new ArgumentNullException(nameof(eventsSelector));
        _eventApplier = eventApplier ?? throw new ArgumentNullException(nameof(eventApplier));
    }

    public (TAggregate, IEnumerable<TEventBase>) GetUpdatedStateAndEvents(
        TCommandResult commandResult, TAggregate aggregate)
    {
        var events = _eventsSelector(commandResult, aggregate);
        var updatedState = aggregate;
        var appliedEvents = new List<TEventBase>();

        foreach (var @event in events)
        {
            updatedState = _eventApplier(aggregate, @event);
            appliedEvents.Add(@event);
        }

        return (updatedState, appliedEvents);
    }
}