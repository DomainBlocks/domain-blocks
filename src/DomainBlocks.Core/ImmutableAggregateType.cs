namespace DomainBlocks.Core;

public interface IImmutableAggregateType<TAggregate> : IAggregateType<TAggregate>
{
    IImmutableCommandResultType<TAggregate, TCommandResult> GetCommandResultType<TCommandResult>();
}

public sealed class ImmutableAggregateType<TAggregate, TEventBase> :
    AggregateTypeBase<TAggregate, TEventBase>, IImmutableAggregateType<TAggregate>
{
    public ImmutableAggregateType()
    {
    }

    private ImmutableAggregateType(ImmutableAggregateType<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
    }

    public override ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        return new ImmutableCommandExecutionContext<TAggregate, TEventBase>(aggregate, this);
    }

    public new IImmutableCommandResultType<TAggregate, TCommandResult> GetCommandResultType<TCommandResult>()
    {
        var commandResultType = base.GetCommandResultType<TCommandResult>();
        return (IImmutableCommandResultType<TAggregate, TCommandResult>)commandResultType;
    }

    protected override ImmutableAggregateType<TAggregate, TEventBase> Clone()
    {
        return new ImmutableAggregateType<TAggregate, TEventBase>(this);
    }
}