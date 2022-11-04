using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public class MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> :
    IMutableCommandResultOptions<TAggregate, IEnumerable<TEventBase>> where TEventBase : class
{
    private EventEnumerationMode _mode = EventEnumerationMode.ApplyAfterEnumerating;

    public MutableEventEnumerableCommandResultOptions()
    {
    }

    private MutableEventEnumerableCommandResultOptions(
        MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> copyFrom)
    {
        _mode = copyFrom._mode;
    }

    public MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> WithEventEnumerationMode(
        EventEnumerationMode mode)
    {
        return new MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase>(this)
        {
            _mode = mode
        };
    }

    public Type ClrType => typeof(IEnumerable<TEventBase>);

    public IEnumerable<TEventBase> Coerce(IEnumerable<TEventBase> commandResult, IEnumerable<object> raisedEvents)
    {
        return raisedEvents.Cast<TEventBase>();
    }

    public IReadOnlyCollection<object> SelectEventsAndUpdateState(
        IEnumerable<TEventBase> commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        return _mode switch
        {
            EventEnumerationMode.None => commandResult.ToList().AsReadOnly(),
            EventEnumerationMode.ApplyAfterEnumerating => ApplyAfterEnumerating(commandResult, state, eventApplier),
            EventEnumerationMode.ApplyWhileEnumerating => ApplyWhileEnumerating(commandResult, state, eventApplier),
            _ => throw new InvalidOperationException($"Unknown enum value {nameof(EventEnumerationMode)}.{_mode}.")
        };
    }

    private static IReadOnlyCollection<object> ApplyAfterEnumerating(
        IEnumerable<TEventBase> commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        var events = commandResult.ToList().AsReadOnly();

        foreach (var @event in events)
        {
            eventApplier(state, @event);
        }

        return events;
    }

    private static IReadOnlyCollection<object> ApplyWhileEnumerating(
        IEnumerable<TEventBase> commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        var appliedEvents = new List<object>();

        foreach (var @event in commandResult)
        {
            eventApplier(state, @event);
            appliedEvents.Add(@event);
        }

        return appliedEvents.AsReadOnly();
    }
}