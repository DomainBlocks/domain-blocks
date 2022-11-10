using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core;

public sealed class MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> :
    IMutableCommandResultOptions<TAggregate, TCommandResult> where TEventBase : class
{
    private bool _isApplyEventsEnabled;
    private Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;

    public MutableCommandResultOptions()
    {
    }

    private MutableCommandResultOptions(MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> copyFrom)
    {
        _isApplyEventsEnabled = copyFrom._isApplyEventsEnabled;
        _eventsSelector = copyFrom._eventsSelector;
    }

    public Type ClrType => typeof(TCommandResult);

    public MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> WithApplyEventsEnabled(bool isEnabled)
    {
        return new MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult>(this)
        {
            _isApplyEventsEnabled = isEnabled
        };
    }

    public MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult> WithEventsSelector(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        if (eventsSelector == null) throw new ArgumentNullException(nameof(eventsSelector));

        return new MutableCommandResultOptions<TAggregate, TEventBase, TCommandResult>(this)
        {
            _eventsSelector = eventsSelector
        };
    }

    public IReadOnlyCollection<object> SelectEventsAndUpdateState(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        var events = _eventsSelector(commandResult).ToList().AsReadOnly();

        if (_isApplyEventsEnabled)
        {
            foreach (var @event in events)
            {
                eventApplier(state, @event);
            }
        }

        return events;
    }
}