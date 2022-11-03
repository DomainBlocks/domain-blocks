using System;

namespace DomainBlocks.Core;

public interface IImmutableAggregateType<TAggregate> : IAggregateType<TAggregate>
{
    public IImmutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>();
}

public class ImmutableAggregateType<TAggregate, TEventBase> :
    AggregateTypeBase<TAggregate, TEventBase>,
    IImmutableAggregateType<TAggregate>
{
    private Func<TAggregate, TEventBase, TAggregate> _eventApplier;

    public ImmutableAggregateType()
    {
    }

    private ImmutableAggregateType(ImmutableAggregateType<TAggregate, TEventBase> copyFrom) :
        base(copyFrom)
    {
        _eventApplier = copyFrom._eventApplier;
    }

    public ImmutableAggregateType<TAggregate, TEventBase> WithEventApplier(
        Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        var clone = Clone();
        clone._eventApplier = eventApplier;
        return clone;
    }

    public override TAggregate ApplyEvent(TAggregate aggregate, object @event)
    {
        return _eventApplier(aggregate, (TEventBase)@event);
    }

    public override ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate)
    {
        return new ImmutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IImmutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>()
    {
        return (IImmutableCommandReturnType<TAggregate, TCommandResult>)base.GetCommandReturnType<TCommandResult>();
    }

    protected override ImmutableAggregateType<TAggregate, TEventBase> Clone()
    {
        return new ImmutableAggregateType<TAggregate, TEventBase>(this);
    }
}