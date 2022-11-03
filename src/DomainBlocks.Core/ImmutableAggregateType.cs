namespace DomainBlocks.Core;

public interface IImmutableAggregateType<TAggregate> : IAggregateType<TAggregate>
{
    public IImmutableCommandReturnType<TAggregate, TCommandResult> GetCommandReturnType<TCommandResult>();
}

public class ImmutableAggregateType<TAggregate, TEventBase> :
    AggregateTypeBase<TAggregate, TEventBase>,
    IImmutableAggregateType<TAggregate>
{
    public ImmutableAggregateType()
    {
    }

    private ImmutableAggregateType(ImmutableAggregateType<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
    }

    public override ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate)
    {
        return new ImmutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IImmutableCommandReturnType<TAggregate, TCommandResult>
        GetCommandReturnType<TCommandResult>()
    {
        return (IImmutableCommandReturnType<TAggregate, TCommandResult>)base
            .GetCommandReturnType<TCommandResult>();
    }

    protected override ImmutableAggregateType<TAggregate, TEventBase> Clone()
    {
        return new ImmutableAggregateType<TAggregate, TEventBase>(this);
    }
}