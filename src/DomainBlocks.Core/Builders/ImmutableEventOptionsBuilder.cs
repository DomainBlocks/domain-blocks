using System;

namespace DomainBlocks.Core.Builders;

public interface IImmutableApplyEventBuilder<TAggregate, out TEvent>
{
    IEventNameBuilder ApplyWith(Func<TAggregate, TEvent, TAggregate> eventApplier);
}

public sealed class ImmutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> :
    IEventOptionsBuilder<TAggregate>,
    IImmutableApplyEventBuilder<TAggregate, TEvent>,
    IEventNameBuilder
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