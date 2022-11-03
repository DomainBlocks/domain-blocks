using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public interface IMutableCommandResultOptions<TAggregate, in TCommandResult> : ICommandResultOptions
{
    public IEnumerable<object> SelectEventsAndUpdateState(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier);
}

public class MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult>
    : IMutableCommandResultOptions<TAggregate, TCommandResult> where TEventBase : class
{
    private ApplyRaisedEventsBehavior _applyRaisedEventsBehavior;
    private Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;

    public MutableCommandResultOptions()
    {
    }

    private MutableCommandResultOptions(MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> copyFrom)
    {
        _applyRaisedEventsBehavior = copyFrom._applyRaisedEventsBehavior;
        _eventsSelector = copyFrom._eventsSelector;
    }

    public Type ClrType => typeof(TCommandResult);

    public MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> WithApplyRaisedEventsBehavior(
        ApplyRaisedEventsBehavior behavior)
    {
        return new MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult>(this)
        {
            _applyRaisedEventsBehavior = behavior
        };
    }

    public MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> WithEventsSelector(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        return new MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult>(this)
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