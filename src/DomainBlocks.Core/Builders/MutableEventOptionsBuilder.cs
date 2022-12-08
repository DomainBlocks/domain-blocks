using System;

namespace DomainBlocks.Core.Builders;

public interface IMutableApplyEventBuilder<out TAggregate, out TEvent>
{
    IEventNameBuilder ApplyWith(Action<TAggregate, TEvent> eventApplier);
}

public sealed class MutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> :
    IEventOptionsBuilder<TAggregate>,
    IMutableApplyEventBuilder<TAggregate, TEvent>,
    IEventNameBuilder
    where TEvent : TEventBase
{
    private EventOptions<TAggregate, TEventBase, TEvent> _options = new();

    EventOptions<TAggregate> IEventOptionsBuilder<TAggregate>.Options => _options.HideEventType();

    public IEventNameBuilder ApplyWith(Action<TAggregate, TEvent> eventApplier)
    {
        _options = _options.WithEventApplier(eventApplier);
        return this;
    }

    public void HasName(string eventName)
    {
        _options = _options.WithEventName(eventName);
    }
}