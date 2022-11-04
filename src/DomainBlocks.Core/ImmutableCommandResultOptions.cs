using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public class ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> :
    IImmutableCommandResultOptions<TAggregate, TCommandResult> where TEventBase : class
{
    private Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private Func<TCommandResult, TAggregate> _updatedStateSelector;

    public ImmutableCommandResultOptions()
    {
    }

    private ImmutableCommandResultOptions(
        ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> copyFrom)
    {
        _eventsSelector = copyFrom._eventsSelector;
        _updatedStateSelector = copyFrom._updatedStateSelector;
    }
    
    public Type ClrType => typeof(TCommandResult);

    public TCommandResult Coerce(TCommandResult commandResult, IEnumerable<object> raisedEvents)
    {
        if (typeof(TCommandResult) == typeof(IEnumerable<TEventBase>))
        {
            return (TCommandResult)raisedEvents.Cast<TEventBase>();
        }

        return commandResult;
    }

    public ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> WithEventsSelector(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        return new ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult>(this)
        {
            _eventsSelector = eventsSelector
        };
    }

    public ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> WithUpdatedStateSelector(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        return new ImmutableCommandResultOptions<TAggregate, TEventBase, TCommandResult>(this)
        {
            _updatedStateSelector = updatedStateSelector
        };
    }

    public (IReadOnlyCollection<object>, TAggregate) SelectEventsAndUpdateState(
        TCommandResult commandResult, TAggregate state, Func<TAggregate, object, TAggregate> eventApplier)
    {
        var events = _eventsSelector(commandResult);

        if (_updatedStateSelector != null)
        {
            var updatedState = _updatedStateSelector(commandResult);
            return (events.ToList().AsReadOnly(), updatedState);
        }

        {
            var updatedState = state;
            var appliedEvents = new List<object>();

            foreach (var @event in events)
            {
                updatedState = eventApplier(updatedState, @event);
                appliedEvents.Add(@event);
            }

            return (appliedEvents.AsReadOnly(), updatedState);
        }
    }
}