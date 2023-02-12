namespace DomainBlocks.Core;

public sealed class MutableCommandResultType<TAggregate, TEventBase, TCommandResult> :
    IMutableCommandResultType<TAggregate, TCommandResult> where TEventBase : class
{
    private bool _isApplyEventsEnabled;
    private Func<TCommandResult, IEnumerable<TEventBase>>? _eventsSelector;

    public MutableCommandResultType()
    {
    }

    private MutableCommandResultType(MutableCommandResultType<TAggregate, TEventBase, TCommandResult> copyFrom)
    {
        _isApplyEventsEnabled = copyFrom._isApplyEventsEnabled;
        _eventsSelector = copyFrom._eventsSelector;
    }

    public Type ClrType => typeof(TCommandResult);

    public MutableCommandResultType<TAggregate, TEventBase, TCommandResult> SetApplyEventsEnabled(bool isEnabled)
    {
        return new MutableCommandResultType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _isApplyEventsEnabled = isEnabled
        };
    }

    public MutableCommandResultType<TAggregate, TEventBase, TCommandResult> SetEventsSelector(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        if (eventsSelector == null) throw new ArgumentNullException(nameof(eventsSelector));

        return new MutableCommandResultType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _eventsSelector = eventsSelector
        };
    }

    public IReadOnlyCollection<object> SelectEventsAndUpdateStateIfRequired(
        TCommandResult commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        if (_eventsSelector == null)
        {
            throw new InvalidOperationException("No events selector specified");
        }

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