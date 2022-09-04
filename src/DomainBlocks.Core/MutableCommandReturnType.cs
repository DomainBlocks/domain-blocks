using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public interface IMutableCommandReturnType<in TAggregate, in TCommandResult> : ICommandReturnType
{
    public IEnumerable<object> SelectEventsAndUpdateState(TCommandResult commandResult, TAggregate state);
}

public class MutableCommandReturnType<TAggregate, TEventBase, TCommandResult>
    : IMutableCommandReturnType<TAggregate, TCommandResult> where TEventBase : class
{
    private readonly MutableApplyEventsBehavior _applyEventsBehavior;
    private readonly Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private readonly Action<TAggregate, TEventBase> _eventApplier;

    public MutableCommandReturnType(
        MutableApplyEventsBehavior applyEventsBehavior,
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector,
        Action<TAggregate, TEventBase> eventApplier)
    {
        _applyEventsBehavior = applyEventsBehavior;
        _eventsSelector = eventsSelector;
        _eventApplier = eventApplier;
    }

    public Type ClrType => typeof(TCommandResult);

    public IEnumerable<object> SelectEventsAndUpdateState(TCommandResult commandResult, TAggregate state)
    {
        return _applyEventsBehavior switch
        {
            MutableApplyEventsBehavior.None => _eventsSelector(commandResult),
            MutableApplyEventsBehavior.ApplyAfterEnumerating => ApplyAfterEnumerating(commandResult, state),
            MutableApplyEventsBehavior.ApplyWhileEnumerating => ApplyWhileEnumerating(commandResult, state),
            _ => throw new InvalidOperationException(
                $"Unknown enum value {nameof(MutableApplyEventsBehavior)}.{_applyEventsBehavior}.")
        };
    }

    private IEnumerable<object> ApplyAfterEnumerating(TCommandResult commandResult, TAggregate state)
    {
        var events = _eventsSelector(commandResult).ToList();
        foreach (var @event in events)
        {
            _eventApplier(state, @event);
        }

        return events;
    }

    private IEnumerable<object> ApplyWhileEnumerating(TCommandResult commandResult, TAggregate state)
    {
        var events = _eventsSelector(commandResult);
        foreach (var @event in events)
        {
            _eventApplier(state, @event);
            yield return @event;
        }
    }
}