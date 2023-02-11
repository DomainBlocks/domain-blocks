namespace DomainBlocks.Core;

public sealed class MutableEventEnumerableCommandResultType<TAggregate, TEventBase> :
    IMutableCommandResultType<TAggregate, IEnumerable<TEventBase>> where TEventBase : class
{
    private ApplyEventsBehavior _behavior = ApplyEventsBehavior.Never;

    public MutableEventEnumerableCommandResultType()
    {
    }

    private MutableEventEnumerableCommandResultType(
        MutableEventEnumerableCommandResultType<TAggregate, TEventBase> copyFrom)
    {
        _behavior = copyFrom._behavior;
    }

    public MutableEventEnumerableCommandResultType<TAggregate, TEventBase> SetApplyEventsBehavior(
        ApplyEventsBehavior behavior)
    {
        return new MutableEventEnumerableCommandResultType<TAggregate, TEventBase>(this)
        {
            _behavior = behavior
        };
    }

    public Type ClrType => typeof(IEnumerable<TEventBase>);

    public IReadOnlyCollection<object> SelectEventsAndUpdateStateIfRequired(
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