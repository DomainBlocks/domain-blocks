using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IImmutableCommandReturnType<TAggregate, in TCommandResult> : ICommandReturnType
{
    public (IEnumerable<object>, TAggregate) SelectEventsAndUpdateState(TCommandResult commandResult, TAggregate state);
}

public class ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult>
    : IImmutableCommandReturnType<TAggregate, TCommandResult> where TEventBase : class
{
    private Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private Func<TCommandResult, TAggregate> _updatedStateSelector;
    private Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public ImmutableCommandReturnType()
    {
    }
    
    private ImmutableCommandReturnType(
        ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult> copyFrom)
    {
        _eventsSelector = copyFrom._eventsSelector;
        _updatedStateSelector = copyFrom._updatedStateSelector;
        _eventApplier = copyFrom._eventApplier;
    }

    public Type ClrType => typeof(TCommandResult);

    public ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult> WithEventSelector(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        return new ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _eventsSelector = eventsSelector
        };
    }
    
    public ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult> WithUpdatedStateSelector(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        return new ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _updatedStateSelector = updatedStateSelector
        };
    }
    
    public ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult> WithEventsApplied(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        return new ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _eventApplier = eventApplier
        };
    }

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