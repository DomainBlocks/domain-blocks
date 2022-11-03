namespace DomainBlocks.Core;

public interface IImmutableAggregateOptions<TAggregate> : IAggregateOptions<TAggregate>
{
    public IImmutableCommandResultOptions<TAggregate, TCommandResult> GetCommandResultOptions<TCommandResult>();
}

public class ImmutableAggregateOptions<TAggregate, TEventBase> :
    AggregateOptionsBase<TAggregate, TEventBase>,
    IImmutableAggregateOptions<TAggregate>
{
    public ImmutableAggregateOptions()
    {
    }

    private ImmutableAggregateOptions(ImmutableAggregateOptions<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
    }

    public override ICommandExecutionContext<TAggregate> GetCommandExecutionContext(TAggregate aggregate)
    {
        return new ImmutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IImmutableCommandResultOptions<TAggregate, TCommandResult>
        GetCommandResultOptions<TCommandResult>()
    {
        return (IImmutableCommandResultOptions<TAggregate, TCommandResult>)base
            .GetCommandResultOptions<TCommandResult>();
    }

    protected override ImmutableAggregateOptions<TAggregate, TEventBase> Clone()
    {
        return new ImmutableAggregateOptions<TAggregate, TEventBase>(this);
    }
}