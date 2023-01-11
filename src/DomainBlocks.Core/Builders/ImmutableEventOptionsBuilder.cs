namespace DomainBlocks.Core.Builders;

public interface IImmutableEventBuilder<TAggregate, out TEvent> : IEventNameBuilder
{
    IEventNameBuilder ApplyWith(Func<TAggregate, TEvent, TAggregate> eventApplier);
}

public sealed class ImmutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> :
    IEventOptionsBuilder<TAggregate>,
    IImmutableEventBuilder<TAggregate, TEvent>
    where TEvent : TEventBase
{
    private EventOptions<TAggregate, TEventBase, TEvent> _options = new();

    EventOptions<TAggregate> IEventOptionsBuilder<TAggregate>.Options => _options.HideEventType();

    public IEventNameBuilder ApplyWith(Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        _options = _options.WithEventApplier(eventApplier);
        return this;
    }

    public void HasName(string eventName)
    {
        _options = _options.WithEventName(eventName);
    }
}