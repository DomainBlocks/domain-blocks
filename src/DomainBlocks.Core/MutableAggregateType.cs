namespace DomainBlocks.Core;

public interface IMutableAggregateType<TAggregate> : IAggregateType<TAggregate>
{
    bool CanSelectRaisedEventsFromAggregate { get; }

    new void InvokeEventApplier(TAggregate aggregate, object @event);
    IMutableCommandResultType<TAggregate, TCommandResult> GetCommandResultType<TCommandResult>();
    IEnumerable<object> SelectRaisedEvents(TAggregate aggregate);
}

public sealed class MutableAggregateType<TAggregate, TEventBase> :
    AggregateTypeBase<TAggregate, TEventBase>,
    IMutableAggregateType<TAggregate> where TEventBase : class
{
    private Func<TAggregate, IReadOnlyCollection<TEventBase>>? _raisedEventsSelector;

    public MutableAggregateType()
    {
    }

    private MutableAggregateType(MutableAggregateType<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
        _raisedEventsSelector = copyFrom._raisedEventsSelector;
    }

    public bool CanSelectRaisedEventsFromAggregate => _raisedEventsSelector != null;

    public MutableAggregateType<TAggregate, TEventBase> SetEventApplier(Action<TAggregate, TEventBase> eventApplier)
    {
        if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));

        return (MutableAggregateType<TAggregate, TEventBase>)SetEventApplier((agg, e) =>
        {
            eventApplier(agg, e);
            return agg;
        });
    }

    public MutableAggregateType<TAggregate, TEventBase> SetRaisedEventsSelector(
        Func<TAggregate, IReadOnlyCollection<TEventBase>> raisedEventsSelector)
    {
        if (raisedEventsSelector == null) throw new ArgumentNullException(nameof(raisedEventsSelector));

        var clone = Clone();
        clone._raisedEventsSelector = raisedEventsSelector;
        return clone;
    }

    public override ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        return new MutableCommandExecutionContext<TAggregate, TEventBase>(aggregate, this);
    }

    public new void InvokeEventApplier(TAggregate aggregate, object @event) =>
        base.InvokeEventApplier(aggregate, @event);

    public new IMutableCommandResultType<TAggregate, TCommandResult> GetCommandResultType<TCommandResult>()
    {
        var commandResultType = base.GetCommandResultType<TCommandResult>();
        return (IMutableCommandResultType<TAggregate, TCommandResult>)commandResultType;
    }

    public IEnumerable<object> SelectRaisedEvents(TAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));

        if (_raisedEventsSelector == null)
        {
            throw new InvalidOperationException("Aggregate has no events selector specified");
        }

        return _raisedEventsSelector(aggregate);
    }

    protected override MutableAggregateType<TAggregate, TEventBase> Clone()
    {
        return new MutableAggregateType<TAggregate, TEventBase>(this);
    }
}