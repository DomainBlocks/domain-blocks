namespace DomainBlocks.Core.Builders;

public class MutableEventEnumerableCommandResultOptionsBuilder<TAggregate, TEventBase> :
    ICommandResultOptionsBuilder where TEventBase : class
{
    private MutableEventEnumerableCommandResultOptions<TAggregate, TEventBase> _options = new();

    ICommandResultOptions ICommandResultOptionsBuilder.Options => _options;

    public void DoNotApplyEvents()
    {
        _options = _options.WithEventEnumerationMode(EventEnumerationMode.DoNotApply);
    }

    public void ApplyEventsAfterEnumerating()
    {
        _options = _options.WithEventEnumerationMode(EventEnumerationMode.ApplyAfterEnumerating);
    }

    public void ApplyEventsWhileEnumerating()
    {
        _options = _options.WithEventEnumerationMode(EventEnumerationMode.ApplyWhileEnumerating);
    }
}