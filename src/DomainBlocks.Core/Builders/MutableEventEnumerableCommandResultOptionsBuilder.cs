namespace DomainBlocks.Core.Builders;

public sealed class MutableEventEnumerableCommandResultOptionsBuilder<TAggregate, TEventBase> :
    ICommandResultOptionsBuilder where TEventBase : class
{
    private MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> _options = new();

    ICommandResultOptions ICommandResultOptionsBuilder.Options => _options;

    /// <summary>
    /// Specify to apply the events to the aggregate.
    /// </summary>
    /// <param name="behavior">An optional value indicating the behavior to use when applying events.</param>
    public void ApplyEvents(ApplyEventsBehavior behavior = ApplyEventsBehavior.MaterializeFirst)
    {
        _options = _options.WithApplyEventsBehavior(behavior);
    }
}