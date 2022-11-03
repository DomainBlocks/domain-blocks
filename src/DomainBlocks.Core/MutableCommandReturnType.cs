using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public interface IMutableCommandReturnType<TAggregate, in TCommandResult> : ICommandReturnType
{
    public IEnumerable<object> SelectEventsAndUpdateState(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier);
}

public class MutableCommandReturnType<TAggregate, TEventBase, TCommandResult>
    : IMutableCommandReturnType<TAggregate, TCommandResult> where TEventBase : class
{
    private ApplyRaisedEventsBehavior _applyRaisedEventsBehavior;
    private Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;

    public MutableCommandReturnType()
    {
    }
    
    private MutableCommandReturnType(MutableCommandReturnType<TAggregate, TEventBase, TCommandResult> copyFrom)
    {
        _applyRaisedEventsBehavior = copyFrom._applyRaisedEventsBehavior;
        _eventsSelector = copyFrom._eventsSelector;
    }

    public Type ClrType => typeof(TCommandResult);

    public MutableCommandReturnType<TAggregate, TEventBase, TCommandResult> WithApplyRaisedEventsBehavior(
        ApplyRaisedEventsBehavior behavior)
    {
        return new MutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _applyRaisedEventsBehavior = behavior
        };
    }
    
    public MutableCommandReturnType<TAggregate, TEventBase, TCommandResult> WithEventsSelector(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        return new MutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _eventsSelector = eventsSelector
        };
    }

    public IEnumerable<object> SelectEventsAndUpdateState(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        return _applyRaisedEventsBehavior switch
        {
            ApplyRaisedEventsBehavior.None => _eventsSelector(commandResult),
            ApplyRaisedEventsBehavior.ApplyAfterEnumerating =>
                ApplyAfterEnumerating(commandResult, state, eventApplier),
            ApplyRaisedEventsBehavior.ApplyWhileEnumerating =>
                ApplyWhileEnumerating(commandResult, state, eventApplier),
            _ => throw new InvalidOperationException(
                $"Unknown enum value {nameof(ApplyRaisedEventsBehavior)}.{_applyRaisedEventsBehavior}.")
        };
    }

    private IEnumerable<object> ApplyAfterEnumerating(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        var events = _eventsSelector(commandResult).ToList();
        foreach (var @event in events)
        {
            eventApplier(state, @event);
        }

        return events;
    }

    private IEnumerable<object> ApplyWhileEnumerating(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        var events = _eventsSelector(commandResult);
        foreach (var @event in events)
        {
            eventApplier(state, @event);
            yield return @event;
        }
    }
}