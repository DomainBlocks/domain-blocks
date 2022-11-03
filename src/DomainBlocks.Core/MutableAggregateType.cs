using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface IMutableAggregateType<TAggregate> : IAggregateType<TAggregate>
{
    public bool CanSelectRaisedEventsFromAggregate { get; }

    public new void ApplyEvent(TAggregate aggregate, object @event);
    public IMutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>();
    public IEnumerable<object> SelectRaisedEvents(TAggregate aggregate);
}

public class MutableAggregateType<TAggregate, TEventBase> :
    AggregateTypeBase<TAggregate, TEventBase>,
    IMutableAggregateType<TAggregate> where TEventBase : class
{
    private Action<TAggregate, TEventBase> _eventApplier;
    private Func<TAggregate, IEnumerable<TEventBase>> _raisedEventsSelector;

    public MutableAggregateType()
    {
    }

    private MutableAggregateType(MutableAggregateType<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
        _raisedEventsSelector = copyFrom._raisedEventsSelector;
    }

    public bool CanSelectRaisedEventsFromAggregate => _raisedEventsSelector != null;

    public MutableAggregateType<TAggregate, TEventBase> WithEventApplier(
        Action<TAggregate, TEventBase> eventApplier)
    {
        var clone = Clone();
        clone._eventApplier = eventApplier;
        return clone;
    }

    public MutableAggregateType<TAggregate, TEventBase> WithRaisedEventsSelector(
        Func<TAggregate, IEnumerable<TEventBase>> raisedEventsSelector)
    {
        var clone = Clone();
        clone._raisedEventsSelector = raisedEventsSelector;
        return clone;
    }

    public override TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        ((IMutableAggregateType<TAggregate>)this).ApplyEvent(aggregate, @event);
        return aggregate;
    }

    void IMutableAggregateType<TAggregate>.ApplyEvent(TAggregate aggregate, object @event)
    {
        _eventApplier(aggregate, (TEventBase)@event);
    }

    public override ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate)
    {
        return new MutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IMutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>()
    {
        return (IMutableCommandReturnType<TAggregate, TCommandResult>)base.GetCommandReturnType<TCommandResult>();
    }

    public IEnumerable<object> SelectRaisedEvents(TAggregate aggregate)
    {
        if (!CanSelectRaisedEventsFromAggregate)
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