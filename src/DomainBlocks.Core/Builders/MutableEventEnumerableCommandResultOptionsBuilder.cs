namespace DomainBlocks.Core.Builders;

public sealed class MutableEventEnumerableCommandResultOptionsBuilder<TAggregate, TEventBase> :
    ICommandResultOptionsBuilder where TEventBase : class
{
    private MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> _options = new();

    ICommandResultOptions ICommandResultOptionsBuilder.Options => _options;

    /// <summary>
    /// Specify to not apply the returned events to the aggregate. This is the default behaviour.
    /// </summary>
    public void DoNotApplyEvents()
    {
        _options = _options.WithEventEnumerationMode(EventEnumerationMode.DoNotApply);
    }

    /// <summary>
    /// Specify to apply the events to the aggregate after the returned event enumerable has been materialized. This
    /// avoids intermediate state changes if events are yield returned from the command method.
    /// </summary>
    public void ApplyEventsAfterEnumerating()
    {
        _options = _options.WithEventEnumerationMode(EventEnumerationMode.ApplyAfterEnumerating);
    }

    /// <summary>
    /// Specify to apply the events to the aggregate while enumerating through the returned event enumerable. Use this
    /// option to enable intermediate state changes as events are yield returned from the command method.
    /// </summary>
    public void ApplyEventsWhileEnumerating()
    {
        _options = _options.WithEventEnumerationMode(EventEnumerationMode.ApplyWhileEnumerating);
    }
}