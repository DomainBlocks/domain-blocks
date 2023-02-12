namespace DomainBlocks.Core.Builders;

public sealed class MutableEventEnumerableCommandResultTypeBuilder<TAggregate, TEventBase> :
    ICommandResultTypeBuilder where TEventBase : class
{
    private MutableEventEnumerableCommandResultType<TAggregate, TEventBase> _commandResultType = new();

    ICommandResultType ICommandResultTypeBuilder.CommandResultType => _commandResultType;

    /// <summary>
    /// Specify to apply the events to the aggregate.
    /// </summary>
    /// <param name="behavior">An optional value indicating the behavior to use when applying events.</param>
    public void ApplyEvents(ApplyEventsBehavior behavior = ApplyEventsBehavior.MaterializeFirst)
    {
        _commandResultType = _commandResultType.SetApplyEventsBehavior(behavior);
    }
}