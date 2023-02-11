namespace DomainBlocks.Core;

public sealed class ImmutableCommandResultType<TAggregate, TEventBase, TCommandResult> :
    IImmutableCommandResultType<TAggregate, TCommandResult> where TEventBase : class
{
    private Func<TCommandResult, IEnumerable<TEventBase>>? _eventsSelector;
    private Func<TCommandResult, TAggregate>? _updatedStateSelector;

    public ImmutableCommandResultType()
    {
    }

    private ImmutableCommandResultType(
        ImmutableCommandResultType<TAggregate, TEventBase, TCommandResult> copyFrom)
    {
        _eventsSelector = copyFrom._eventsSelector;
        _updatedStateSelector = copyFrom._updatedStateSelector;
    }

    public Type ClrType => typeof(TCommandResult);

    public ImmutableCommandResultType<TAggregate, TEventBase, TCommandResult> SetEventsSelector(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        if (eventsSelector == null) throw new ArgumentNullException(nameof(eventsSelector));

        return new ImmutableCommandResultType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _eventsSelector = eventsSelector
        };
    }

    public ImmutableCommandResultType<TAggregate, TEventBase, TCommandResult> SetUpdatedStateSelector(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        if (updatedStateSelector == null) throw new ArgumentNullException(nameof(updatedStateSelector));

        return new ImmutableCommandResultType<TAggregate, TEventBase, TCommandResult>(this)
        {
            _updatedStateSelector = updatedStateSelector
        };
    }

    public IReadOnlyCollection<object> SelectEventsAndUpdateStateIfRequired(
        TCommandResult commandResult, ref TAggregate state, Func<TAggregate, object, TAggregate> eventApplier)
    {
        if (_eventsSelector == null)
        {
            throw new InvalidOperationException("No events selector specified");
        }

        var events = _eventsSelector(commandResult);

        if (_updatedStateSelector != null)
        {
            state = _updatedStateSelector(commandResult);
            return events.ToList().AsReadOnly();
        }

        {
            var appliedEvents = new List<object>();

            foreach (var @event in events)
            {
                state = eventApplier(state, @event);
                appliedEvents.Add(@event);
            }

            return appliedEvents.AsReadOnly();
        }
    }
}