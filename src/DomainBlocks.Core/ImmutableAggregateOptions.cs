namespace DomainBlocks.Core;

public interface IImmutableAggregateOptions<TAggregate> : IAggregateOptions<TAggregate>
{
    IImmutableCommandResultOptions<TAggregate, TCommandResult> GetCommandResultOptions<TCommandResult>();
}

public sealed class ImmutableAggregateOptions<TAggregate, TEventBase> :
    AggregateOptionsBase<TAggregate, TEventBase>, IImmutableAggregateOptions<TAggregate>
{
    public ImmutableAggregateOptions()
    {
    }

    private ImmutableAggregateOptions(ImmutableAggregateOptions<TAggregate, TEventBase> copyFrom) : base(copyFrom)
    {
    }

    public override ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate)
    {
        return new ImmutableCommandExecutionContext<TAggregate>(aggregate, this);
    }

    public new IImmutableCommandResultOptions<TAggregate, TCommandResult> GetCommandResultOptions<TCommandResult>()
    {
        var commandResultOptions = base.GetCommandResultOptions<TCommandResult>();
        return (IImmutableCommandResultOptions<TAggregate, TCommandResult>)commandResultOptions;
    }

    protected override ImmutableAggregateOptions<TAggregate, TEventBase> Clone()
    {
        return new ImmutableAggregateOptions<TAggregate, TEventBase>(this);
    }
}