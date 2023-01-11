namespace DomainBlocks.Core;

public sealed class MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> :
    IMutableCommandResultOptions<TAggregate, IEnumerable<TEventBase>> where TEventBase : class
{
    private ApplyEventsBehavior _behavior = ApplyEventsBehavior.Never;

    public MutableEventEnumerableCommandResultOptions()
    {
    }

    private MutableEventEnumerableCommandResultOptions(
        MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> copyFrom)
    {
        _behavior = copyFrom._behavior;
    }

    public MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> WithApplyEventsBehavior(
        ApplyEventsBehavior behavior)
    {
        return new MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase>(this)
        {
            _behavior = behavior
        };
    }

    public Type ClrType => typeof(IEnumerable<TEventBase>);

    public IReadOnlyCollection<object> SelectEventsAndUpdateState(
        IEnumerable<TEventBase> commandResult, TAggregate state, Action<TAggregate, object> eventApplier)
    {
        return _behavior switch
        {
            ApplyEventsBehavior.Never => commandResult.ToList().AsReadOnly(),
            ApplyEventsBehavior.MaterializeFirst => ApplyAfterEnumerating(commandResult, state, eventApplier),
            ApplyEventsBehavior.WhileEnumerating => ApplyWhileEnumerating(commandResult, state, eventApplier),
            _ => throw new InvalidOperationException($"Unknown enum value {nameof(ApplyEventsBehavior)}.{_behavior}.")
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