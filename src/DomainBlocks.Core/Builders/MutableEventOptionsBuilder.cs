using System;

namespace DomainBlocks.Core.Builders;

public sealed class MutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> :
    IEventOptionsBuilder<TAggregate, TEventBase>
    where TEvent : TEventBase
{
    private EventOptions<TAggregate, TEventBase, TEvent> _options = new();

    EventOptions<TAggregate> IEventOptionsBuilder<TAggregate, TEventBase>.Options => _options.HideEventType();

    public MutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> ApplyWith(Action<TAggregate, TEvent> eventApplier)
    {
        _options = _options.WithEventApplier(eventApplier);
        return this;
    }

    public MutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> HasName(string eventName)
    {
        _options = _options.WithEventName(eventName);
        return this;
    }
}