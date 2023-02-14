namespace DomainBlocks.Core.Builders;

public sealed class ImmutableAggregateEventTypeBuilder<TAggregate, TEventBase, TEvent> :
    IAggregateEventTypeBuilder<TAggregate>,
    IImmutableAggregateEventTypeBuilder<TAggregate, TEvent>
    where TEvent : TEventBase
{
    private AggregateEventType<TAggregate, TEventBase, TEvent> _eventType = new();

    AggregateEventType<TAggregate> IAggregateEventTypeBuilder<TAggregate>.EventType => _eventType.HideGenericType();

    public IEventNameBuilder ApplyWith(Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        _eventType = _eventType.SetEventApplier(eventApplier);
        return this;
    }

    public void HasName(string eventName)
    {
        _eventType = _eventType.SetEventName(eventName);
    }
}