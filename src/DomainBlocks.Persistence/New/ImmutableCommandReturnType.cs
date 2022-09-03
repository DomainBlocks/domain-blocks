using System;
using System.Collections.Generic;

namespace DomainBlocks.Persistence.New;

public interface IImmutableCommandReturnType<TAggregate, in TCommandResult> : ICommandReturnType
{
    public (IEnumerable<object>, TAggregate) SelectEventsAndUpdateState(TCommandResult commandResult, TAggregate state);
}

public class ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult>
    : IImmutableCommandReturnType<TAggregate, TCommandResult> where TEventBase : class
{
    private readonly Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private readonly Func<TCommandResult, TAggregate> _updatedStateSelector;
    private readonly Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public ImmutableCommandReturnType(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector,
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        _eventsSelector = eventsSelector;
        _updatedStateSelector = updatedStateSelector;
    }

    public ImmutableCommandReturnType(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector,
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _eventsSelector = eventsSelector;
        _eventApplier = eventApplier;
    }

    public Type ClrType => typeof(TCommandResult);

    public (IEnumerable<object>, TAggregate) SelectEventsAndUpdateState(TCommandResult commandResult, TAggregate state)
    {
        var events = _eventsSelector(commandResult);

        if (_updatedStateSelector != null)
        {
            var updatedState = _updatedStateSelector(commandResult);
            return (events, updatedState);
        }

        {
            var updatedState = state;
            var appliedEvents = new List<object>();

            foreach (var @event in events)
            {
                updatedState = _eventApplier(updatedState, @event);
                appliedEvents.Add(@event);
            }

            return (appliedEvents, updatedState);
        }
    }
}